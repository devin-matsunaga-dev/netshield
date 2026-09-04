"""Cisco IOS and IOS-XE."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from typing import ClassVar, Final

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.snmp.tables import text
from collector.vendors.base import SnmpVendorAdapter

CHASSIS_ID: Final = "1.3.6.1.4.1.9.3.6.3.0"
"""OLD-CISCO-CHASSIS-MIB ``chassisId``. Carries the serial on platforms with no ENTITY-MIB."""

_VERSION: Final = re.compile(r"Version\s+([^\s,]+)")
"""``sysDescr`` on IOS reads "… Software … Version 15.7(3)M2, RELEASE SOFTWARE …"."""


class CiscoIosAdapter(SnmpVendorAdapter):
    """IOS and IOS-XE, which share a product arc and a description format."""

    vendor: ClassVar[str] = "CiscoIos"

    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.DISCOVER, JobKind.POLL, JobKind.CONFIG_FETCH}
    )

    # ciscoProducts. NX-OS sits under 1.3.6.1.4.1.9.12.3.1.3 instead, which is why this arc is
    # the narrow 9.1 rather than all of enterprise 9 — see cisco_nxos.py.
    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ("1.3.6.1.4.1.9.1",)

    system_description_markers: ClassVar[tuple[str, ...]] = (
        "Cisco IOS Software",
        "IOS-XE Software",
        "Cisco Internetwork Operating System",
    )

    def scalar_oids(self) -> tuple[str, ...]:
        return (CHASSIS_ID,)

    def describe(
        self,
        system: SystemGroup,
        scalars: Mapping[str, str],
        entities: Sequence[PhysicalEntity],
    ) -> VendorFacts:
        base = self.from_chassis(entities)
        version = _VERSION.search(system.descr or "")

        return VendorFacts(
            model=base.model,
            os_version=version.group(1).rstrip(",") if version else base.os_version,
            serial_number=base.serial_number or text(dict(scalars), CHASSIS_ID),
        )
