"""The range sweep: what it expands, what it skips, and what it reports.

No packet leaves this file. ``collector.discovery.sweep`` is exercised against a stand-in for
:func:`collector.icmp.probe.probe`, which is the seam the socket work sits behind — the probe
itself has its own tests against recorded bytes in ``test_icmp_probe.py``.
"""

from __future__ import annotations

import asyncio
from typing import Any

import pytest

from collector.discovery.sweep import (
    MAX_SWEEP_ADDRESSES,
    SweepError,
    addresses,
    sweep,
)
from collector.icmp.probe import IcmpUnavailableError, ProbeOutcome, ProbeReply

# --- Expanding a span --------------------------------------------------------------------------


def test_a_span_expands_to_every_address_between_its_ends_inclusive() -> None:
    assert list(addresses("192.0.2.1", "192.0.2.4", [])) == [
        "192.0.2.1",
        "192.0.2.2",
        "192.0.2.3",
        "192.0.2.4",
    ]


def test_a_span_of_one_address_expands_to_that_address() -> None:
    assert list(addresses("192.0.2.7", "192.0.2.7", [])) == ["192.0.2.7"]


def test_an_excluded_block_is_left_out_of_the_expansion() -> None:
    """An exclusion says 'do not send a packet here', so the address is never probed at all."""
    expanded = list(addresses("192.0.2.0", "192.0.2.7", ["192.0.2.4/30"]))

    assert expanded == ["192.0.2.0", "192.0.2.1", "192.0.2.2", "192.0.2.3"]


def test_a_bare_address_as_an_exclusion_removes_exactly_that_address() -> None:
    expanded = list(addresses("192.0.2.1", "192.0.2.3", ["192.0.2.2"]))

    assert expanded == ["192.0.2.1", "192.0.2.3"]


def test_an_exclusion_whose_host_bits_are_set_is_read_as_the_block_around_them() -> None:
    """The API normalises before it sends; this is the safe reading of one that arrives raw."""
    assert list(addresses("192.0.2.0", "192.0.2.3", ["192.0.2.1/30"])) == []


def test_an_exclusion_outside_the_span_removes_nothing() -> None:
    assert list(addresses("192.0.2.1", "192.0.2.2", ["198.51.100.0/24"])) == [
        "192.0.2.1",
        "192.0.2.2",
    ]


def test_an_ipv6_span_expands_the_same_way() -> None:
    assert list(addresses("2001:db8::1", "2001:db8::3", [])) == [
        "2001:db8::1",
        "2001:db8::2",
        "2001:db8::3",
    ]


def test_a_span_that_runs_backwards_is_refused() -> None:
    with pytest.raises(SweepError, match="backwards"):
        list(addresses("192.0.2.9", "192.0.2.1", []))


def test_a_span_of_two_families_is_refused() -> None:
    with pytest.raises(SweepError, match="family"):
        list(addresses("192.0.2.1", "2001:db8::1", []))


def test_a_span_larger_than_the_ceiling_is_refused_before_anything_is_probed() -> None:
    """A job the API never meant to send fails with a reason rather than one address at a time."""
    with pytest.raises(SweepError, match=str(MAX_SWEEP_ADDRESSES)):
        list(addresses("10.0.0.0", "10.255.255.255", []))


def test_something_that_is_not_an_address_is_refused() -> None:
    with pytest.raises(SweepError, match="not an IP address"):
        list(addresses("lab-sw-01", "192.0.2.1", []))


def test_something_that_is_not_a_block_is_refused_as_an_exclusion() -> None:
    with pytest.raises(SweepError, match="not an IP address or a CIDR block"):
        list(addresses("192.0.2.1", "192.0.2.2", ["not-a-block"]))


# --- Sweeping ----------------------------------------------------------------------------------


def _answered(address: str, rtt: float) -> ProbeOutcome:
    return ProbeOutcome(
        address=address,
        sent=1,
        received=1,
        replies=(ProbeReply(sequence=0, rtt_milliseconds=rtt),),
    )


def _silent(address: str) -> ProbeOutcome:
    return ProbeOutcome(
        address=address,
        sent=1,
        received=0,
        replies=(ProbeReply(sequence=0, rtt_milliseconds=None),),
    )


def _probes(
    monkeypatch: pytest.MonkeyPatch,
    answering: dict[str, float],
    *,
    raises: dict[str, BaseException] | None = None,
) -> list[str]:
    """Stands in for the socket layer, and records every address it was asked about."""
    asked: list[str] = []

    async def fake(address: str, **_: Any) -> ProbeOutcome:
        asked.append(address)

        if raises is not None and address in raises:
            raise raises[address]

        return _answered(address, answering[address]) if address in answering else _silent(address)

    monkeypatch.setattr("collector.discovery.sweep.probe", fake)

    return asked


async def test_only_the_addresses_that_answered_are_reported(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Silence is the absence of an observation, which is why a /16 does not write 65,000 rows."""
    _probes(monkeypatch, {"192.0.2.2": 1.5, "192.0.2.4": 9.0})

    outcome = await sweep("192.0.2.1", "192.0.2.4", concurrency=4)

    assert [responder.address for responder in outcome.responders] == ["192.0.2.2", "192.0.2.4"]
    assert [responder.rtt_milliseconds for responder in outcome.responders] == [1.5, 9.0]
    assert outcome.scanned == 4
    assert outcome.excluded == 0
    assert outcome.truncated is False


async def test_an_excluded_address_is_never_probed(monkeypatch: pytest.MonkeyPatch) -> None:
    asked = _probes(monkeypatch, {})

    outcome = await sweep("192.0.2.0", "192.0.2.3", exclusions=["192.0.2.2/31"])

    assert asked == ["192.0.2.0", "192.0.2.1"]
    assert outcome.scanned == 2
    assert outcome.excluded == 2


async def test_a_span_where_nothing_answers_is_a_result_rather_than_a_failure(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A hundred per cent silence is evidence about the range. The job succeeded."""
    _probes(monkeypatch, {})

    outcome = await sweep("192.0.2.1", "192.0.2.3")

    assert outcome.responders == ()
    assert outcome.scanned == 3


async def test_being_unable_to_probe_at_all_fails_the_sweep(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A collector with no ICMP socket must not be able to report the estate as empty."""
    _probes(
        monkeypatch,
        {},
        raises={"192.0.2.1": IcmpUnavailableError("no socket")},
    )

    with pytest.raises(IcmpUnavailableError):
        await sweep("192.0.2.1", "192.0.2.3")


async def test_one_address_the_kernel_refuses_counts_as_silence(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """One address of many failing locally is not a reason to lose the rest of the span."""
    asked = _probes(
        monkeypatch,
        {"192.0.2.3": 2.0},
        raises={"192.0.2.2": OSError("network is unreachable")},
    )

    outcome = await sweep("192.0.2.1", "192.0.2.3")

    assert asked == ["192.0.2.1", "192.0.2.2", "192.0.2.3"]
    assert [responder.address for responder in outcome.responders] == ["192.0.2.3"]


async def test_more_responders_than_allowed_are_truncated_and_said_to_be(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The ceiling keeps one job's payload under what the API will store for it."""
    _probes(monkeypatch, {f"192.0.2.{last}": 1.0 for last in range(1, 5)})

    outcome = await sweep("192.0.2.1", "192.0.2.4", max_responders=2)

    assert len(outcome.responders) == 2
    assert outcome.truncated is True


async def test_concurrency_bounds_how_many_probes_are_in_flight_at_once(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """What makes 'a run over a /24 completes within the configured window' a property."""
    peak = 0
    running = 0

    async def fake(address: str, **_: Any) -> ProbeOutcome:
        nonlocal peak, running

        running += 1
        peak = max(peak, running)

        try:
            await asyncio.sleep(0)

            return _silent(address)
        finally:
            running -= 1

    monkeypatch.setattr("collector.discovery.sweep.probe", fake)

    await sweep("192.0.2.1", "192.0.2.16", concurrency=4)

    assert peak <= 4
