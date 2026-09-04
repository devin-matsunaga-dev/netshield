"""The socket layer, against a socket pair standing in for the network.

CONVENTIONS.md §7 forbids testing a protocol interaction against a live device, and a probe of
127.0.0.1 would be one — it would also pass or fail depending on whether the machine running the
suite permits unprivileged ICMP, which is the opposite of a gate. So the probe is given a real
file descriptor with a fake device on the other end of it: the event loop does real non-blocking
I/O, and what answers is a fixture that replies exactly as much as the test wants it to.
"""

from __future__ import annotations

import asyncio
import socket
from collections.abc import Callable, Iterator
from typing import Any

import pytest

import collector.icmp.probe as probe_module
from collector.icmp.packet import ECHO_REPLY_V4, HEADER_LENGTH, checksum
from collector.icmp.probe import IcmpUnavailableError, probe

ADDRESS = "192.0.2.10"
"""TEST-NET-1. Never routed, and nothing here sends to it anyway."""


class _NoConnect(socket.socket):
    """A socket whose ``connect`` does nothing, because a socket pair is already connected."""

    def connect(self, address: Any) -> None:
        return None


@pytest.fixture
def wire() -> Iterator[tuple[socket.socket, socket.socket]]:
    """A connected pair: the probe's end, and the end the fake device answers on."""
    left, right = socket.socketpair(socket.AF_UNIX, socket.SOCK_DGRAM)

    probe_side = _NoConnect(socket.AF_UNIX, socket.SOCK_DGRAM, 0, fileno=left.detach())
    right.setblocking(False)

    try:
        yield probe_side, right
    finally:
        probe_side.close()
        right.close()


@pytest.fixture
def connected(
    wire: tuple[socket.socket, socket.socket],
    monkeypatch: pytest.MonkeyPatch,
) -> tuple[socket.socket, socket.socket]:
    """Hands the probe the pair's near end instead of opening an ICMP socket."""
    probe_side, device_side = wire

    monkeypatch.setattr(probe_module, "_open", lambda family: (probe_side, False))

    return probe_side, device_side


async def _device(
    device_side: socket.socket,
    answers: Callable[[int], bool],
    corrupt_token: bool = False,
    duplicate: bool = False,
) -> None:
    """Answer echo requests, for the sequences ``answers`` says yes to."""
    loop = asyncio.get_running_loop()

    while True:
        request = await loop.sock_recv(device_side, 1500)

        identifier = int.from_bytes(request[4:6])
        sequence = int.from_bytes(request[6:8])
        payload = bytes(len(request) - HEADER_LENGTH) if corrupt_token else request[HEADER_LENGTH:]

        if not answers(sequence):
            continue

        reply = _echo_reply(identifier, sequence, payload)

        await loop.sock_sendall(device_side, reply)

        if duplicate:
            await loop.sock_sendall(device_side, reply)


def _echo_reply(identifier: int, sequence: int, payload: bytes) -> bytes:
    """The device's answer, checksummed the way a device would checksum it."""
    unsummed = (
        bytes([ECHO_REPLY_V4, 0, 0, 0, *identifier.to_bytes(2), *sequence.to_bytes(2)]) + payload
    )

    return unsummed[:2] + checksum(unsummed).to_bytes(2) + unsummed[4:]


async def _run(
    device_side: socket.socket,
    count: int,
    answers: Callable[[int], bool],
    reply_timeout_seconds: float = 0.4,
    duplicate: bool = False,
    corrupt_token: bool = False,
) -> Any:
    device = asyncio.create_task(
        _device(device_side, answers, corrupt_token=corrupt_token, duplicate=duplicate)
    )

    try:
        return await probe(
            ADDRESS,
            count=count,
            reply_timeout_seconds=reply_timeout_seconds,
            interval_seconds=0,
        )
    finally:
        device.cancel()

        await asyncio.gather(device, return_exceptions=True)


async def test_every_request_answered_reports_no_loss_and_an_rtt_per_request(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    _, device_side = connected

    outcome = await _run(device_side, count=4, answers=lambda _: True)

    assert outcome.sent == 4
    assert outcome.received == 4
    assert outcome.loss_percent == 0.0
    assert [reply.sequence for reply in outcome.replies] == [0, 1, 2, 3]
    assert all(reply.rtt_milliseconds is not None for reply in outcome.replies)


async def test_some_requests_answered_reports_partial_loss_and_which_went_unanswered(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    # This is the observation the whole state machine turns on: partial loss is a third thing,
    # neither up nor down, and the per-request detail is what makes it readable afterwards.
    _, device_side = connected

    outcome = await _run(device_side, count=4, answers=lambda sequence: sequence % 2 == 0)

    assert outcome.sent == 4
    assert outcome.received == 2
    assert outcome.loss_percent == 50.0

    answered = [reply.sequence for reply in outcome.replies if reply.rtt_milliseconds is not None]
    assert answered == [0, 2]


async def test_nothing_answering_reports_total_loss_rather_than_raising(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    # A device that says nothing is a successful probe with a clear answer. Only an inability to
    # probe at all raises, because only that is evidence about the collector.
    _, device_side = connected

    outcome = await _run(device_side, count=2, answers=lambda _: False, reply_timeout_seconds=0.1)

    assert outcome.sent == 2
    assert outcome.received == 0
    assert outcome.loss_percent == 100.0
    assert all(reply.rtt_milliseconds is None for reply in outcome.replies)


async def test_a_reply_that_arrives_twice_is_counted_once(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    _, device_side = connected

    outcome = await _run(
        device_side,
        count=2,
        answers=lambda _: True,
        duplicate=True,
        reply_timeout_seconds=0.1,
    )

    assert outcome.received == 2
    assert outcome.received <= outcome.sent


async def test_a_reply_carrying_another_runs_payload_is_ignored(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    # A shared raw socket sees every process's ping. The payload token is what keeps another
    # one's replies out of this probe's arithmetic.
    _, device_side = connected

    outcome = await _run(
        device_side,
        count=2,
        answers=lambda _: True,
        corrupt_token=True,
        reply_timeout_seconds=0.1,
    )

    assert outcome.received == 0


async def test_the_probe_returns_as_soon_as_every_reply_is_in(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    _, device_side = connected

    loop = asyncio.get_running_loop()
    started = loop.time()

    await _run(device_side, count=2, answers=lambda _: True, reply_timeout_seconds=5)

    assert loop.time() - started < 1


async def test_probing_something_that_is_not_an_address_raises() -> None:
    # The API hands the collector an address, never a name. There is no resolver here, and a job
    # carrying a hostname is a malformed job rather than an unreachable device.
    with pytest.raises(ValueError, match="does not appear to be"):
        await probe("switch-01.example.net", count=1, reply_timeout_seconds=0.1, interval_seconds=0)


async def test_no_icmp_socket_raises_rather_than_reporting_loss(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The distinction the whole package rests on. A collector with no ICMP privilege must fail
    # its jobs, not report five hundred devices as offline.
    def refuse(*_: object, **__: object) -> socket.socket:
        raise PermissionError("no")

    # Targeted by name so that only the probe module's view of socket.socket is replaced.
    monkeypatch.setattr("collector.icmp.probe.socket.socket", refuse)

    with pytest.raises(IcmpUnavailableError, match="CAP_NET_RAW"):
        await probe(ADDRESS, count=1, reply_timeout_seconds=0.1, interval_seconds=0)


async def test_the_interval_between_requests_is_honoured(
    connected: tuple[socket.socket, socket.socket],
) -> None:
    _, device_side = connected

    device = asyncio.create_task(_device(device_side, lambda _: True))
    loop = asyncio.get_running_loop()
    started = loop.time()

    try:
        outcome = await probe(
            ADDRESS,
            count=3,
            reply_timeout_seconds=0.5,
            interval_seconds=0.05,
        )
    finally:
        device.cancel()

        await asyncio.gather(device, return_exceptions=True)

    # Two gaps between three requests.
    assert loop.time() - started >= 0.1
    assert outcome.sent == 3
