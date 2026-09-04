"""Cisco NX-OS."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from typing import ClassVar, Final

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.vendors.base import SnmpVendorAdapter

_VERSION: Final = re.compile(r"(?:System version|Version)\s+([^\s,]+)")
"""``sysDescr`` reads "Cisco NX-OS(tm) n9000, Software (n9000-dk9), Version 9.3(5), RELEASE …"."""


class CiscoNxOsAdapter(SnmpVendorAdapter):
    """NX-OS, which shares enterprise 9 with IOS and nothing else."""

    vendor: ClassVar[str] = "CiscoNxOs"

    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.DISCOVER, JobKind.POLL, JobKind.CONFIG_FETCH}
    )

    # cevChassis, under CISCO-ENTITY-VENDORTYPE-OID-MIB, which is where a Nexus reports itself.
    # Longer than the IOS arc, so the registry's longest-prefix rule picks this one for a Nexus
    # even though both adapters are Cisco.
    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ("1.3.6.1.4.1.9.12.3.1.3",)

    system_description_markers: ClassVar[tuple[str, ...]] = ("NX-OS",)

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
            serial_number=base.serial_number,
        )
