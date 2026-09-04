"""ICMP echo, encoded and decoded.

RFC 792 for IPv4 and RFC 4443 for IPv6, and nothing else. This module knows how to build an echo
request, how to read an echo reply, and how to compute the RFC 1071 checksum that sits between
them. It opens no socket, keeps no state and measures no time — everything here is a function
from bytes to bytes, which is what makes it testable against fixtures rather than against a
network (CONVENTIONS.md §7).

It is deliberately not a general ICMP library. There is no destination-unreachable handling, no
timestamp or address-mask message, no fragmentation and no traceroute: NetShield asks one
question of a device, and every message type that is not an answer to it is simply not a reply.
"""

from __future__ import annotations

import socket
import struct
from dataclasses import dataclass
from typing import Final

ECHO_REQUEST_V4: Final = 8
ECHO_REPLY_V4: Final = 0
ECHO_REQUEST_V6: Final = 128
ECHO_REPLY_V6: Final = 129

_HEADER: Final = struct.Struct("!BBHHH")
"""type, code, checksum, identifier, sequence — eight octets, the same layout in both families."""

HEADER_LENGTH: Final = _HEADER.size


@dataclass(frozen=True, slots=True)
class EchoReply:
    """One echo reply, as it came off the wire."""

    identifier: int
    sequence: int
    payload: bytes


def checksum(data: bytes) -> int:
    """The RFC 1071 one's-complement sum, as ICMPv4 requires.

    An odd-length message is padded with a zero octet for the sum only; the padding is not part
    of the message.
    """
    if len(data) % 2:
        data += b"\x00"

    total = 0

    for (word,) in struct.iter_unpack("!H", data):
        total += word

    # Fold the carries back in, twice, because folding once can itself carry.
    total = (total >> 16) + (total & 0xFFFF)
    total += total >> 16

    return ~total & 0xFFFF


def build_echo_request(
    family: socket.AddressFamily,
    identifier: int,
    sequence: int,
    payload: bytes,
) -> bytes:
    """One echo request, ready to send.

    The checksum is computed for IPv4 and left at zero for IPv6. That is not an omission: an
    ICMPv6 checksum covers a pseudo-header containing the source address, which the process
    sending does not choose and cannot know before the kernel has picked a route — so the kernel
    computes it, and a value written here would be overwritten at best and wrong at worst.
    """
    request_type = ECHO_REQUEST_V6 if family == socket.AF_INET6 else ECHO_REQUEST_V4

    if family == socket.AF_INET6:
        return _HEADER.pack(request_type, 0, 0, identifier, sequence) + payload

    unsummed = _HEADER.pack(request_type, 0, 0, identifier, sequence) + payload

    return _HEADER.pack(request_type, 0, checksum(unsummed), identifier, sequence) + payload


def parse_echo_reply(message: bytes, family: socket.AddressFamily) -> EchoReply | None:
    """Read an echo reply, or ``None`` if that is not what this message is.

    ``None`` covers everything that is not an answer to our question: a truncated datagram, an
    ICMP error, a reply for the other address family, and — for IPv4, where it can be checked —
    a message whose checksum does not hold. The caller counts those as no reply rather than as a
    failure, because a device that sends something unexpected has still not told us it is there.

    The IPv6 checksum is not verified, for the reason it is not computed above: the pseudo-header
    it covers is not available here. The kernel has already validated it before delivery.
    """
    if len(message) < HEADER_LENGTH:
        return None

    message_type, code, carried, identifier, sequence = _HEADER.unpack_from(message)

    expected_type = ECHO_REPLY_V6 if family == socket.AF_INET6 else ECHO_REPLY_V4

    if message_type != expected_type or code != 0:
        return None

    if family == socket.AF_INET and not _checksum_holds(message, carried):
        return None

    return EchoReply(identifier, sequence, message[HEADER_LENGTH:])


def strip_ipv4_header(datagram: bytes) -> bytes:
    """Take the IPv4 header off a datagram read from a raw socket.

    A raw IPv4 socket delivers the IP header along with the payload; an unprivileged ICMP
    datagram socket does not, and neither does a raw IPv6 socket. The caller knows which kind of
    socket it opened, so this is applied deliberately rather than guessed at from the bytes.
    """
    if not datagram:
        return datagram

    # The low nibble of the first octet is the header length in 32-bit words.
    header_length = (datagram[0] & 0x0F) * 4

    return datagram[header_length:] if header_length <= len(datagram) else b""


def _checksum_holds(message: bytes, carried: int) -> bool:
    """Whether the message sums to the checksum it carries."""
    without = message[:2] + b"\x00\x00" + message[4:]

    return checksum(without) == carried
