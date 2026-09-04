"""One walk, from the first read to the answer: which vendor, what model, which interfaces.

The order matters and is the whole design. The system group is read first because
``sysObjectID`` is what decides who answers for the device; only then is the vendor's own MIB
read, because until an adapter has been chosen there is nothing to say which private OIDs are
worth asking for. A device that answers the system group and nothing else still produces a
fingerprint — a reduced one, correctly labelled.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Final

import structlog

from collector.snmp import oids
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts, physical_entities
from collector.snmp.interfaces import InterfaceRecord, interfaces
from collector.snmp.session import SnmpError, SnmpSession
from collector.vendors.base import VendorRegistry

_LOG: Final = structlog.get_logger(__name__)


@dataclass(frozen=True, slots=True)
class WalkOutcome:
    """Everything one walk established about one device."""

    vendor: str
    reduced_capability: bool
    system: SystemGroup
    facts: VendorFacts
    interfaces: tuple[InterfaceRecord, ...] = ()
    interface_count: int = 0
    interfaces_truncated: bool = False
    entities: tuple[PhysicalEntity, ...] = field(default=())


async def walk_device(
    session: SnmpSession,
    vendors: VendorRegistry,
    *,
    max_rows: int,
    max_interfaces: int,
) -> WalkOutcome:
    """Walk one device and resolve what it is.

    Raises :class:`SnmpError` when the device could not be walked at all. That is deliberately
    different from walking a device that answers very little: the first leaves the device's
    recorded fingerprint untouched, and the second replaces it with what was actually seen.
    """
    system = SystemGroup.parse(await session.get(list(oids.SYSTEM_SCALARS)))

    if system.object_id is None and system.descr is None:
        # An agent that answers the read but has neither of the two objects every SNMP device is
        # required to implement is not a device this can fingerprint. Reporting it as a generic
        # device with nothing known would overwrite whatever a previous walk had established.
        raise SnmpError(
            "The device answered the system group with neither sysObjectID nor sysDescr."
        )

    adapter = vendors.resolve(system)

    if adapter is None:
        raise SnmpError("This collector has no vendor adapters registered, not even generic SNMP.")

    scalars = await session.get(list(adapter.scalar_oids()))
    entities = physical_entities(await session.walk(oids.ENT_PHYSICAL_TABLE, max_rows=max_rows))
    facts = adapter.describe(system, scalars, entities)

    found = interfaces(
        await session.walk(oids.IF_TABLE, max_rows=max_rows),
        await session.walk(oids.IF_X_TABLE, max_rows=max_rows),
    )

    _LOG.info(
        "collector.snmp.fingerprinted",
        vendor=adapter.vendor,
        reducedCapability=adapter.reduced_capability,
        sysObjectId=system.object_id,
        interfaces=len(found),
    )

    return WalkOutcome(
        vendor=adapter.vendor,
        reduced_capability=adapter.reduced_capability,
        system=system,
        facts=facts,
        interfaces=found[:max_interfaces],
        interface_count=len(found),
        interfaces_truncated=len(found) > max_interfaces,
        entities=entities,
    )
