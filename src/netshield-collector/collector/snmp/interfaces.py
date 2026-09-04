"""The interface inventory: ``ifTable`` and ``ifXTable``, joined on ``ifIndex``.

One record per interface the device reports, with the columns SPEC.md §2 asks an inventory to
carry. Nothing here is vendor-specific — RFC 2863 is the same everywhere, which is exactly why
the interface inventory works on a device NetShield cannot otherwise identify.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass

from collector.snmp import oids
from collector.snmp.tables import number, rows, text

_MEGABIT: int = 1_000_000
"""``ifHighSpeed`` is megabits per second; everything NetShield stores is bits per second."""

_IF_SPEED_CEILING: int = 4_294_967_295
"""``ifSpeed`` is a 32-bit gauge, so it saturates here and lies about anything faster."""


@dataclass(frozen=True, slots=True)
class InterfaceRecord:
    """One interface, as the device describes it."""

    index: int
    name: str | None = None
    description: str | None = None
    alias: str | None = None
    interface_type: int | None = None
    mtu: int | None = None
    speed_bits_per_second: int | None = None
    physical_address: str | None = None
    admin_status: int | None = None
    oper_status: int | None = None


def interfaces(
    if_table: Mapping[str, str],
    if_x_table: Mapping[str, str],
) -> tuple[InterfaceRecord, ...]:
    """Join the two tables into records, in ``ifIndex`` order.

    Every interface the walk returned is here. Bounding how many of them are *reported* is the
    caller's decision, because it is the caller that knows what the API asked for; bounding how
    many are *read* is the walk's, through its row ceiling.

    ``ifXTable`` is optional. A device that implements only ``ifTable`` — which is any agent old
    enough, and most simulated ones — yields records with no name and no alias rather than none
    at all.

    An interface whose index will not parse as an integer is dropped. ``ifIndex`` is defined as
    an integer and a row keyed by anything else is an agent NetShield cannot join two tables for.
    """
    base = rows(if_table, oids.IF_TABLE)
    extended = rows(if_x_table, oids.IF_X_TABLE)

    records: list[InterfaceRecord] = []

    for index, row in base.items():
        parsed = _index(index)

        if parsed is None:
            continue

        extra = extended.get(index, {})

        records.append(
            InterfaceRecord(
                index=parsed,
                name=text(extra, oids.IF_NAME),
                description=text(row, oids.IF_DESCR),
                alias=text(extra, oids.IF_ALIAS),
                interface_type=number(row, oids.IF_TYPE),
                mtu=number(row, oids.IF_MTU),
                speed_bits_per_second=_speed(row, extra),
                physical_address=text(row, oids.IF_PHYS_ADDRESS),
                admin_status=number(row, oids.IF_ADMIN_STATUS),
                oper_status=number(row, oids.IF_OPER_STATUS),
            )
        )

    records.sort(key=lambda record: record.index)

    return tuple(records)


def _speed(row: Mapping[str, str], extra: Mapping[str, str]) -> int | None:
    """Bits per second, preferring ``ifHighSpeed`` wherever ``ifSpeed`` cannot express it.

    ``ifSpeed`` saturates at 4.29 Gbit/s, so a 10G port reports the ceiling and a 100G port
    reports the same ceiling. ``ifHighSpeed`` is the RFC 2863 answer and is used whenever the
    device gives one; ``ifSpeed`` stands in for agents that do not implement ``ifXTable``.
    """
    high = number(extra, oids.IF_HIGH_SPEED)

    if high:
        return high * _MEGABIT

    low = number(row, oids.IF_SPEED)

    if low is None:
        return None

    # A saturated gauge with no ifHighSpeed beside it is not a speed, it is the absence of one.
    return None if low >= _IF_SPEED_CEILING else low


def _index(index: str) -> int | None:
    try:
        return int(index)
    except ValueError:
        return None
