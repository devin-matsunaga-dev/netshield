"""MikroTik RouterOS."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from typing import ClassVar, Final

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.snmp.tables import text
from collector.vendors.base import SnmpVendorAdapter

LICENSE_VERSION: Final = "1.3.6.1.4.1.14988.1.1.4.4.0"
"""MIKROTIK-MIB ``mtxrLicVersion`` — the running RouterOS version."""

SERIAL_NUMBER: Final = "1.3.6.1.4.1.14988.1.1.7.3.0"
"""MIKROTIK-MIB ``mtxrSerialNumber``."""

_MODEL: Final = re.compile(r"RouterOS\s+(\S+)")
"""``sysDescr`` reads "RouterOS CCR2004-1G-12S+2XS"."""


class MikroTikRouterOsAdapter(SnmpVendorAdapter):
    """RouterOS, which implements no ENTITY-MIB on most boards and answers from MIKROTIK-MIB."""

    vendor: ClassVar[str] = "MikroTikRouterOs"

    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.DISCOVER, JobKind.POLL, JobKind.CONFIG_FETCH}
    )

    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ("1.3.6.1.4.1.14988",)

    system_description_markers: ClassVar[tuple[str, ...]] = ("RouterOS",)

    def scalar_oids(self) -> tuple[str, ...]:
        return (LICENSE_VERSION, SERIAL_NUMBER)

    def describe(
        self,
        system: SystemGroup,
        scalars: Mapping[str, str],
        entities: Sequence[PhysicalEntity],
    ) -> VendorFacts:
        base = self.from_chassis(entities)
        row = dict(scalars)
        model = _MODEL.search(system.descr or "")

        return VendorFacts(
            model=(model.group(1) if model else None) or base.model,
            os_version=text(row, LICENSE_VERSION) or base.os_version,
            serial_number=text(row, SERIAL_NUMBER) or base.serial_number,
        )
