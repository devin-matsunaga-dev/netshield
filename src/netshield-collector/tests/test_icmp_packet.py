"""The RFC layer, checked as bytes.

Every case here is a fixture rather than a device (CONVENTIONS.md §7). That is the whole reason
this layer was written by hand instead of imported: an echo request either is what RFC 792 says
it is or it is not, and that is a question a test can answer without a network.
"""

from __future__ import annotations

import socket

import pytest

from collector.icmp.packet import (
    ECHO_REPLY_V4,
    ECHO_REPLY_V6,
    ECHO_REQUEST_V4,
    ECHO_REQUEST_V6,
    HEADER_LENGTH,
    build_echo_request,
    checksum,
    parse_echo_reply,
    strip_ipv4_header,
)

PAYLOAD = b"netshield-probe-payload"


def _reply_v4(identifier: int = 0x1234, sequence: int = 0, payload: bytes = PAYLOAD) -> bytes:
    """An IPv4 echo reply with a correct checksum, as a device would send it."""
    unsummed = bytes([ECHO_REPLY_V4, 0, 0, 0, *identifier.to_bytes(2), *sequence.to_bytes(2)])
    unsummed += payload
    summed = checksum(unsummed)

    return unsummed[:2] + summed.to_bytes(2) + unsummed[4:]


def test_a_message_carrying_its_own_checksum_sums_to_it() -> None:
    # RFC 1071: the ones-complement sum of a message carrying its own correct checksum is zero,
    # complemented to 0xFFFF. This is the identity every receiver relies on.
    message = _reply_v4()
    without = message[:2] + b"\x00\x00" + message[4:]

    assert checksum(without) == int.from_bytes(message[2:4])


def test_an_odd_length_message_is_padded_rather_than_refused() -> None:
    assert checksum(b"\x01\x02\x03") == checksum(b"\x01\x02\x03\x00")


def test_the_checksum_of_a_known_message_is_the_hand_computed_one() -> None:
    # Eight octets of an echo request: type 8, code 0, checksum 0, identifier 1, sequence 1.
    # The words are 0x0800, 0x0000, 0x0001, 0x0001; the sum is 0x0802 and its complement 0xF7FD.
    assert checksum(bytes([8, 0, 0, 0, 0, 1, 0, 1])) == 0xF7FD


def test_an_ipv4_request_carries_type_eight_and_a_valid_checksum() -> None:
    request = build_echo_request(socket.AF_INET, 0xBEEF, 7, PAYLOAD)

    assert request[0] == ECHO_REQUEST_V4
    assert request[1] == 0
    assert int.from_bytes(request[4:6]) == 0xBEEF
    assert int.from_bytes(request[6:8]) == 7
    assert request[HEADER_LENGTH:] == PAYLOAD

    # The message including its own checksum sums to zero, which is what a receiver checks.
    without = request[:2] + b"\x00\x00" + request[4:]
    assert checksum(without) == int.from_bytes(request[2:4])


def test_an_ipv6_request_carries_type_128_and_leaves_the_checksum_to_the_kernel() -> None:
    # The ICMPv6 checksum covers a pseudo-header holding the source address, which this process
    # does not choose. The kernel computes it; anything written here would be wrong.
    request = build_echo_request(socket.AF_INET6, 0xBEEF, 3, PAYLOAD)

    assert request[0] == ECHO_REQUEST_V6
    assert int.from_bytes(request[2:4]) == 0


def test_a_well_formed_ipv4_reply_is_parsed() -> None:
    reply = parse_echo_reply(_reply_v4(identifier=0x4321, sequence=5), socket.AF_INET)

    assert reply is not None
    assert reply.identifier == 0x4321
    assert reply.sequence == 5
    assert reply.payload == PAYLOAD


def test_a_reply_with_a_broken_checksum_is_not_a_reply() -> None:
    corrupted = bytearray(_reply_v4())
    corrupted[-1] ^= 0xFF

    assert parse_echo_reply(bytes(corrupted), socket.AF_INET) is None


def test_an_echo_request_is_not_a_reply() -> None:
    # Our own request coming back on a shared raw socket is not an answer to it.
    request = build_echo_request(socket.AF_INET, 1, 1, PAYLOAD)

    assert parse_echo_reply(request, socket.AF_INET) is None


def test_a_destination_unreachable_is_not_a_reply() -> None:
    # Type 3. NetShield asks one question and counts anything that is not an answer as no reply,
    # rather than growing a general ICMP library to interpret every error message.
    unreachable = bytes([3, 1, 0, 0, 0, 0, 0, 0]) + PAYLOAD

    assert parse_echo_reply(unreachable, socket.AF_INET) is None


def test_a_truncated_datagram_is_not_a_reply() -> None:
    assert parse_echo_reply(b"\x00\x00\x00", socket.AF_INET) is None


def test_an_ipv6_reply_read_as_ipv4_is_not_a_reply() -> None:
    # Type 129 is an echo reply in v6 and something else entirely in v4.
    reply = bytes([ECHO_REPLY_V6, 0, 0, 0, 0, 1, 0, 1]) + PAYLOAD

    assert parse_echo_reply(reply, socket.AF_INET) is None


def test_an_ipv6_reply_skips_the_checksum_it_cannot_recompute() -> None:
    reply = bytes([ECHO_REPLY_V6, 0, 0xDE, 0xAD, 0, 9, 0, 2]) + PAYLOAD

    parsed = parse_echo_reply(reply, socket.AF_INET6)

    assert parsed is not None
    assert parsed.identifier == 9
    assert parsed.sequence == 2


@pytest.mark.parametrize("words", [5, 6, 15])
def test_the_ipv4_header_removed_is_exactly_the_one_the_ihl_declares(words: int) -> None:
    # A raw IPv4 socket delivers the IP header; the low nibble of its first octet is the header
    # length in 32-bit words, so an options-carrying header is longer than the usual twenty.
    header = bytes([0x40 | words]) + bytes(words * 4 - 1)
    message = _reply_v4()

    assert strip_ipv4_header(header + message) == message


def test_a_datagram_shorter_than_its_declared_header_strips_to_nothing() -> None:
    assert strip_ipv4_header(bytes([0x4F, 0, 0, 0])) == b""


def test_stripping_nothing_yields_nothing() -> None:
    assert strip_ipv4_header(b"") == b""
