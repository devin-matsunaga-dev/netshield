"""Reading a decoded octet string back as the bytes and the numbers it carried.

:func:`collector.snmp.session.decode` renders an octet string as text when every byte is
printable and as colon-separated uppercase hex when any byte is not, because ``ifDescr`` and
``ifPhysAddress`` arrive as the same SNMP type and only one of them is a word. That is the right
default for a value a human reads. It is exactly wrong for a value that was never text in the
first place — a ``PortList`` bitmap, an LLDP capability map, a CDP capability word — and those
have to undo it before they can be read.

This module is that inversion, written once. WP-2.2 discovered it for ``PortList`` and kept it
private to :mod:`collector.snmp.vlans`; WP-2.6 needed the same thing for two capability columns,
and a second copy of a rule this subtle is a second thing that can drift.

Nothing here knows what any value *means*. :func:`octets` gives back bytes, :func:`bit_positions`
reads an ASN.1 ``BITS`` value and :func:`big_endian_int` reads an octet string carrying a number.
Which MIB column is which shape belongs to the module that names the column.
"""

from __future__ import annotations

import re
import string
from typing import Final

# Everything `collector.snmp.session.decode` treats as text: it decodes an octet string as UTF-8
# and keeps it only when every character is printable, rendering anything else as colon-separated
# uppercase hex. `octets` is the inverse of exactly that, which is why this set has to match.
_TEXTUAL: Final = frozenset(string.printable) - frozenset("\x0b\x0c")

_HEX: Final = re.compile(r"^[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2})*$")


def octets(value: str | None) -> bytes:
    """A decoded octet string back as the bytes it came from.

    A bitmap arrives here already decoded, which makes it the one kind of value in this collector
    where ``decode``'s choice has to be undone rather than read. Almost always it is
    colon-separated hex, because a bitmap with an empty group of eight bits contains a zero byte
    and a zero byte is not printable. But a dense bitmap can be all-printable — ``0x41`` is ``A``
    — and would then have been decoded as text.

    So the rule is ``decode`` read backwards: the hex spelling is taken only when it could not
    itself have been produced as text, meaning at least one of the bytes it decodes to is one
    ``decode`` would have hexed. Where both readings are possible the text one wins, because that
    is the one ``decode`` would have chosen. A bitmap that is genuinely ambiguous — five printable
    bytes that also spell valid hex — is a lossiness of the recorded format rather than of this,
    and is documented in the fixture README.
    """
    if not value:
        return b""

    if _HEX.match(value):
        candidate = bytes(int(part, 16) for part in value.split(":"))

        if any(chr(byte) not in _TEXTUAL for byte in candidate):
            return candidate

    return value.encode("utf-8", errors="ignore")


def bit_positions(value: str | None, *, max_octets: int) -> int | None:
    """An ASN.1 ``BITS`` value as a mask whose bit *N* is the named bit *N*.

    ``BITS`` numbers its members from the **most** significant bit of the first octet: bit 0 is
    ``0x80`` of octet 0, bit 7 is ``0x01`` of octet 0, bit 8 is ``0x80`` of octet 1. So an
    ``lldpRemSysCapEnabled`` of ``28:00`` is bits 2 and 4 — bridge and router — and reading the
    two octets as the integer 10240 would name neither.

    The mask returned here is in the ordinary direction, bit *N* worth ``1 << N``, so that
    everything above this module can test a capability without knowing how the wire spelled it.

    ``None`` where there is no value, or where it is longer than the column's declared size: a
    reading that does not fit the shape the MIB defines is one of the values this collector drops
    rather than guesses at.
    """
    raw = octets(value)

    if not raw or len(raw) > max_octets:
        return None

    mask = 0

    for offset, byte in enumerate(raw):
        for bit in range(8):
            if byte & (0x80 >> bit):
                mask |= 1 << (offset * 8 + bit)

    return mask


def big_endian_int(value: str | None, *, max_octets: int) -> int | None:
    """An octet string carrying a big-endian integer, as that integer.

    ``cdpCacheCapabilities`` is this shape rather than ``BITS``: CISCO-CDP-MIB declares it
    ``OCTET STRING (SIZE (4))`` and the four bytes are one 32-bit number whose bit 0 is worth 1,
    which is the opposite end from the bit map above. The two columns look alike and are read two
    different ways, and reading either with the other's rule would name the wrong capabilities
    rather than fail.

    ``None`` where there is no value or it is longer than the column's declared size.
    """
    raw = octets(value)

    if not raw or len(raw) > max_octets:
        return None

    return int.from_bytes(raw, byteorder="big")
