"""What a walk observed, before anybody has decided which vendor it belongs to.

These four shapes are the whole vocabulary between :mod:`collector.snmp` and
:mod:`collector.vendors`. A vendor adapter is handed them and returns :class:`VendorFacts`; it
never sees a socket, a credential or a pysnmp value, which is what keeps a vendor module small
enough to be obviously read-only.
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass

from collector.snmp import oids
from collector.snmp.tables import number, rows, text


@dataclass(frozen=True, slots=True)
class SystemGroup:
    """The system group, which is the first thing read and the thing that decides the rest."""

    descr: str | None = None
    object_id: str | None = None
    uptime_ticks: int | None = None
    name: str | None = None
    contact: str | None = None
    location: str | None = None

    @property
    def uptime_seconds(self) -> float | None:
        """``sysUpTime`` in seconds.

        It is a 32-bit counter of hundredths of a second, so it wraps after about 497 days. This
        reports what the agent said; reconstructing a boot time from a value that wraps is a
        different question and nothing in V1 asks it.
        """
        return round(self.uptime_ticks / 100.0, 2) if self.uptime_ticks is not None else None

    @staticmethod
    def parse(scalars: Mapping[str, str]) -> SystemGroup:
        """The system group as read, with anything the device did not answer left absent."""
        row = dict(scalars)

        return SystemGroup(
            descr=text(row, oids.SYS_DESCR),
            object_id=text(row, oids.SYS_OBJECT_ID),
            uptime_ticks=number(row, oids.SYS_UP_TIME),
            name=text(row, oids.SYS_NAME),
            contact=text(row, oids.SYS_CONTACT),
            location=text(row, oids.SYS_LOCATION),
        )


@dataclass(frozen=True, slots=True)
class PhysicalEntity:
    """One row of ENTITY-MIB's ``entPhysicalTable``."""

    index: str
    descr: str | None = None
    entity_class: int | None = None
    name: str | None = None
    hardware_revision: str | None = None
    firmware_revision: str | None = None
    software_revision: str | None = None
    serial_number: str | None = None
    model_name: str | None = None

    @property
    def is_chassis(self) -> bool:
        """Whether this row describes the box rather than a part of it."""
        return self.entity_class == oids.ENT_PHYSICAL_CLASS_CHASSIS


def physical_entities(walked: Mapping[str, str]) -> tuple[PhysicalEntity, ...]:
    """``entPhysicalTable`` as rows, in index order.

    Devices with no ENTITY-MIB — which is most of the cheap ones and all of the simulated ones —
    answer nothing here, and an empty result is the correct outcome rather than a failure.
    """
    parsed = [
        PhysicalEntity(
            index=index,
            descr=text(row, oids.ENT_PHYSICAL_DESCR),
            entity_class=number(row, oids.ENT_PHYSICAL_CLASS),
            name=text(row, oids.ENT_PHYSICAL_NAME),
            hardware_revision=text(row, oids.ENT_PHYSICAL_HARDWARE_REV),
            firmware_revision=text(row, oids.ENT_PHYSICAL_FIRMWARE_REV),
            software_revision=text(row, oids.ENT_PHYSICAL_SOFTWARE_REV),
            serial_number=text(row, oids.ENT_PHYSICAL_SERIAL_NUM),
            model_name=text(row, oids.ENT_PHYSICAL_MODEL_NAME),
        )
        for index, row in rows(walked, oids.ENT_PHYSICAL_TABLE).items()
    ]

    return tuple(sorted(parsed, key=lambda entity: _index_key(entity.index)))


def chassis(entities: Sequence[PhysicalEntity]) -> PhysicalEntity | None:
    """The entry describing the box.

    The lowest-indexed row whose class is ``chassis``. A stack answers with one chassis row per
    member and the lowest index is the one the estate calls "this device"; a device with no
    chassis row at all — a fixed-configuration switch that fills in only ``module`` entries —
    yields nothing, and the vendor adapter falls back to ``sysDescr``.
    """
    for entity in entities:
        if entity.is_chassis:
            return entity

    return None


@dataclass(frozen=True, slots=True)
class VendorFacts:
    """What a vendor adapter made of a walk. Every member may be absent."""

    model: str | None = None
    os_version: str | None = None
    serial_number: str | None = None


def _index_key(index: str) -> tuple[int, ...]:
    try:
        return tuple(int(part) for part in index.split(".") if part)
    except ValueError:
        return ()
