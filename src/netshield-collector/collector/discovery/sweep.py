"""Probing a run of addresses and reporting which ones answered.

The socket work is :mod:`collector.icmp.probe`'s and is not repeated here. What this module adds
is the three things a sweep needs and a single probe does not: expanding a span into addresses,
dropping the ones an operator excluded, and running many probes at once under a bound.

Two properties it is careful about, because both are the difference between a discovery signal
and a misleading one:

* **Silence is not an error.** An address that does not answer is simply absent from the result.
  Nothing is recorded for it, which is what keeps a /16 sweep from writing sixty-five thousand
  rows to say that nothing happened.
* **Being unable to probe is not silence.** If no ICMP socket can be opened at all, this raises
  rather than reporting an empty range — a collector that has lost a capability must not be able
  to report the estate as empty.
"""

from __future__ import annotations

import asyncio
import ipaddress
from collections.abc import Iterator, Sequence
from dataclasses import dataclass
from typing import Final

import structlog

from collector.icmp.probe import IcmpUnavailableError, probe

_LOG: Final = structlog.get_logger(__name__)

MAX_SWEEP_ADDRESSES: Final = 65_536
"""The largest span this collector will sweep in one job, whatever the API asks for.

The API bounds a job at ``DiscoveryOptions.MaxAddressesPerJob`` and this is far above it. It is
here so that a job carrying a span the API never meant to send — a settings mistake, a hand-made
row — fails immediately with a reason rather than being worked through one address at a time.
"""


class SweepError(RuntimeError):
    """The span cannot be swept as asked, and no address was probed."""


@dataclass(frozen=True, slots=True)
class SweepResponder:
    """One address that answered."""

    address: str
    rtt_milliseconds: float | None


@dataclass(frozen=True, slots=True)
class SweepOutcome:
    """What one sweep of one span observed."""

    first_address: str
    last_address: str
    scanned: int
    excluded: int
    truncated: bool
    responders: tuple[SweepResponder, ...]


def addresses(first: str, last: str, exclusions: Sequence[str]) -> Iterator[str]:
    """The addresses in ``first``..``last`` that no exclusion covers, in order.

    :raises SweepError: either end is not an address, the two are of different families, the
        span runs backwards, an exclusion is not a block, or the span is larger than
        :data:`MAX_SWEEP_ADDRESSES`.
    """
    start = _address(first)
    end = _address(last)

    if start.version != end.version:
        raise SweepError("The two ends of the span are not of the same address family.")

    if int(end) < int(start):
        raise SweepError("The span runs backwards: its last address precedes its first.")

    span = int(end) - int(start) + 1

    if span > MAX_SWEEP_ADDRESSES:
        raise SweepError(
            f"The span holds {span} addresses, which is more than this collector "
            f"will sweep in one job ({MAX_SWEEP_ADDRESSES})."
        )

    blocks = [_network(exclusion) for exclusion in exclusions]

    for number in range(int(start), int(end) + 1):
        candidate = ipaddress.ip_address(number)

        if not any(candidate in block for block in blocks):
            yield str(candidate)


async def sweep(
    first: str,
    last: str,
    *,
    exclusions: Sequence[str] = (),
    count: int = 1,
    reply_timeout_seconds: float = 1.0,
    interval_seconds: float = 0.0,
    concurrency: int = 64,
    max_responders: int = 1024,
) -> SweepOutcome:
    """Probe every address in the span that is not excluded, and report what answered.

    ``concurrency`` is what makes the whole thing fit in a window: 256 addresses at a one-second
    timeout take four seconds at 64 in flight and four minutes at one. It is a bound rather than
    a target — a span smaller than it simply runs all at once.

    :raises SweepError: the span cannot be read, or is larger than this collector will sweep.
    :raises IcmpUnavailableError: no ICMP socket of either kind could be opened, so nothing was
        probed. Reported as a failed job rather than as an empty range.
    """
    targets = list(addresses(first, last, exclusions))
    span = _span(first, last)
    slots = asyncio.Semaphore(max(concurrency, 1))

    async def one(address: str) -> SweepResponder | None:
        async with slots:
            try:
                outcome = await probe(
                    address,
                    count=count,
                    reply_timeout_seconds=reply_timeout_seconds,
                    interval_seconds=interval_seconds,
                )
            except IcmpUnavailableError:
                # The collector cannot probe at all. Raised on rather than swallowed, so the job
                # fails and nothing reads the silence as an estate with nothing in it.
                raise
            except (OSError, ValueError) as error:
                # This one address could not be probed — an unreachable local route, an address
                # the kernel will not accept. It is one address of many, and the rest of the span
                # is still worth sweeping, so it counts as silence.
                _LOG.debug("collector.sweep.address.failed", address=address, error=str(error))

                return None

        if outcome.received == 0:
            return None

        round_trips = outcome.round_trips

        return SweepResponder(address, min(round_trips) if round_trips else None)

    answered = [
        responder
        for responder in await asyncio.gather(*(one(address) for address in targets))
        if responder is not None
    ]

    truncated = len(answered) > max_responders

    _LOG.info(
        "collector.sweep.completed",
        firstAddress=first,
        lastAddress=last,
        scanned=len(targets),
        responded=len(answered),
        truncated=truncated,
    )

    return SweepOutcome(
        first_address=first,
        last_address=last,
        scanned=len(targets),
        excluded=span - len(targets),
        truncated=truncated,
        responders=tuple(answered[:max_responders]),
    )


def _span(first: str, last: str) -> int:
    """How many addresses the span holds, before exclusions."""
    return int(_address(last)) - int(_address(first)) + 1


def _address(value: str) -> ipaddress.IPv4Address | ipaddress.IPv6Address:
    try:
        return ipaddress.ip_address(value)
    except ValueError as error:
        raise SweepError(f"'{value}' is not an IP address.") from error


def _network(value: str) -> ipaddress.IPv4Network | ipaddress.IPv6Network:
    try:
        # Non-strict, so that a block whose host bits are set is read as the block around them
        # rather than refused. The API normalises before it sends, and this is the safe reading
        # of anything that arrives without having been.
        return ipaddress.ip_network(value, strict=False)
    except ValueError as error:
        raise SweepError(f"'{value}' is not an IP address or a CIDR block.") from error
