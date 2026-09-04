"""Sending echo requests at one address and timing what comes back.

The socket orchestration for :mod:`collector.icmp.packet`: open the least privileged socket the
platform will give us, send ``count`` requests spaced by ``interval_seconds``, and wait
``reply_timeout_seconds`` after the last one for the replies still outstanding.

Two things this module is careful about, because both are the difference between a reachability
signal and a misleading one:

* **Every request is accounted for.** The outcome carries one entry per request sent, in order,
  each with its round trip or nothing. A probe that got two replies out of four is a real and
  different observation from one that got four, and the API's state machine reads it as such.
* **Being unable to probe is not packet loss.** If no ICMP socket can be opened at all, this
  raises rather than reporting a hundred per cent loss. The job then fails and the device's state
  is left exactly as it was — a collector that has lost a capability must not be able to report
  the estate as offline.
"""

from __future__ import annotations

import asyncio
import ipaddress
import os
import secrets
import socket
import time
from dataclasses import dataclass
from typing import Final

from collector.icmp.packet import build_echo_request, parse_echo_reply, strip_ipv4_header

TOKEN_LENGTH: Final = 8
"""How much of the payload identifies this probe run."""

PAYLOAD_LENGTH: Final = 56
"""The payload size ``ping`` has used since time immemorial. Familiar in a packet capture."""

_RECEIVE_BUFFER: Final = 1500
"""One Ethernet frame. Nothing this module reads is larger, and a reply that were would not be
one of ours."""


class IcmpUnavailableError(RuntimeError):
    """No ICMP socket could be opened, so no probe was performed.

    Distinct from an unreachable device on purpose. The runner turns this into a failed job, and
    the API records a failed job against the collector rather than against the device.
    """


@dataclass(frozen=True, slots=True)
class ProbeReply:
    """One request and what came back for it."""

    sequence: int
    rtt_milliseconds: float | None


@dataclass(frozen=True, slots=True)
class ProbeOutcome:
    """What one probe of one address observed."""

    address: str
    sent: int
    received: int
    replies: tuple[ProbeReply, ...]

    @property
    def loss_percent(self) -> float:
        """What proportion of the requests went unanswered, 0 to 100."""
        return round(100.0 * (self.sent - self.received) / self.sent, 2) if self.sent else 100.0

    @property
    def round_trips(self) -> tuple[float, ...]:
        """The round trips that were actually measured, in the order they were sent."""
        return tuple(
            reply.rtt_milliseconds for reply in self.replies if reply.rtt_milliseconds is not None
        )


async def probe(
    address: str,
    count: int,
    reply_timeout_seconds: float,
    interval_seconds: float,
) -> ProbeOutcome:
    """Probe ``address`` and report what answered.

    ``reply_timeout_seconds`` is how long to wait after the last request for the replies still
    outstanding — it is part of what a probe *is*, not a budget for the call, which is why it is
    a parameter here rather than an :func:`asyncio.timeout` the caller wraps around it. Wrapping
    would abandon the probe instead of letting it report the replies it did receive, and partial
    loss is the observation this whole package exists to notice. The runner still applies its own
    :func:`asyncio.timeout` above this, as the outer bound on a wedged call (CONVENTIONS.md §5).

    :raises ValueError: ``address`` is not an IP address. The collector is handed an address by
        the API, never a name — there is no resolver here and a job carrying a hostname is a
        malformed job rather than an unreachable device.
    :raises IcmpUnavailableError: no ICMP socket of either kind could be opened.
    """
    parsed = ipaddress.ip_address(address)
    family = socket.AF_INET6 if parsed.version == 6 else socket.AF_INET

    connection, carries_ip_header = _open(family)

    # The identifier the kernel will overwrite on an unprivileged socket, which is why matching
    # does not depend on it: replies are matched on the payload token and the sequence number,
    # both of which survive either kind of socket. It is still written and still read, because a
    # packet capture of a NetShield probe should look like the RFC says it should.
    identifier = os.getpid() & 0xFFFF
    token = secrets.token_bytes(TOKEN_LENGTH)
    payload = token + bytes(PAYLOAD_LENGTH - TOKEN_LENGTH)

    loop = asyncio.get_running_loop()

    sent_at: dict[int, float] = {}
    arrived_at: dict[int, float] = {}
    everything_answered = asyncio.Event()

    try:
        connection.setblocking(False)

        # Connected, so the kernel filters replies to this peer and a second device answering on
        # the same socket cannot be counted as this one.
        connection.connect((address, 0))

        receiver = asyncio.create_task(
            _receive(
                loop,
                connection,
                family=family,
                token=token,
                carries_ip_header=carries_ip_header,
                sent_at=sent_at,
                arrived_at=arrived_at,
                expected=count,
                everything_answered=everything_answered,
            ),
            name="icmp-receive",
        )

        try:
            for sequence in range(count):
                request = build_echo_request(family, identifier, sequence, payload)

                sent_at[sequence] = time.perf_counter()

                await loop.sock_sendall(connection, request)

                if sequence < count - 1 and interval_seconds > 0:
                    await asyncio.sleep(interval_seconds)

            # The timeout runs from the last request, which is what makes it "how long to wait
            # for a reply" rather than a budget the earlier requests have already spent.
            try:
                async with asyncio.timeout(reply_timeout_seconds):
                    await everything_answered.wait()
            except TimeoutError:
                pass
        finally:
            receiver.cancel()

            await asyncio.gather(receiver, return_exceptions=True)
    finally:
        connection.close()

    return _outcome(address, count, sent_at, arrived_at)


def _open(family: socket.AddressFamily) -> tuple[socket.socket, bool]:
    """The least privileged ICMP socket this platform will give us.

    An unprivileged datagram socket first, which is what Linux offers when
    ``net.ipv4.ping_group_range`` includes the running group; a raw socket second, which needs
    ``CAP_NET_RAW``. If neither opens, that is a deployment fact an operator has to be told,
    and the message says which of the two to grant.

    Returns the socket and whether datagrams read from it carry an IP header — a raw IPv4 socket
    delivers one, and nothing else does.
    """
    protocol = socket.IPPROTO_ICMPV6 if family == socket.AF_INET6 else socket.IPPROTO_ICMP

    try:
        return socket.socket(family, socket.SOCK_DGRAM, protocol), False
    except OSError:
        pass

    try:
        connection = socket.socket(family, socket.SOCK_RAW, protocol)
    except OSError as error:
        raise IcmpUnavailableError(
            "No ICMP socket could be opened: neither an unprivileged datagram socket nor a raw "
            "socket is permitted. Grant the process CAP_NET_RAW, or include its group in "
            "net.ipv4.ping_group_range."
        ) from error

    return connection, family == socket.AF_INET


async def _receive(
    loop: asyncio.AbstractEventLoop,
    connection: socket.socket,
    family: socket.AddressFamily,
    token: bytes,
    carries_ip_header: bool,
    sent_at: dict[int, float],
    arrived_at: dict[int, float],
    expected: int,
    everything_answered: asyncio.Event,
) -> None:
    """Read replies until cancelled, recording when each sequence arrived.

    It runs alongside the sending loop rather than after it, so a reply to the first request is
    timed when it actually arrives instead of after the last request has gone out.
    """
    while True:
        datagram = await loop.sock_recv(connection, _RECEIVE_BUFFER)
        received_at = time.perf_counter()

        message = strip_ipv4_header(datagram) if carries_ip_header else datagram
        reply = parse_echo_reply(message, family)

        if reply is None or not reply.payload.startswith(token):
            # Something else on the wire: another process's ping on a shared raw socket, an ICMP
            # error, a stale reply from a previous run. Not an answer to anything we asked.
            continue

        if reply.sequence not in sent_at or reply.sequence in arrived_at:
            # A duplicate, or a sequence we have not sent. Counting either would report more
            # replies than requests.
            continue

        arrived_at[reply.sequence] = received_at

        if len(arrived_at) >= expected:
            everything_answered.set()

            return


def _outcome(
    address: str,
    count: int,
    sent_at: dict[int, float],
    arrived_at: dict[int, float],
) -> ProbeOutcome:
    """One entry per request sent, in order, whether or not it was answered."""
    replies = tuple(
        ProbeReply(
            sequence,
            round((arrived_at[sequence] - sent_at[sequence]) * 1000, 3)
            if sequence in arrived_at
            else None,
        )
        for sequence in range(count)
    )

    return ProbeOutcome(
        address=address,
        sent=len(sent_at),
        received=sum(1 for reply in replies if reply.rtt_milliseconds is not None),
        replies=replies,
    )
