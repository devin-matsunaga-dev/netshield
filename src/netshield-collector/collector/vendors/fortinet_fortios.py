"""Fortinet FortiOS."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from typing import ClassVar, Final

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.snmp.tables import text
from collector.vendors.base import SnmpVendorAdapter

SYS_SERIAL: Final = "1.3.6.1.4.1.12356.100.1.1.1.0"
"""FORTINET-CORE-MIB ``fnSysSerial`` — the serial every Fortinet product answers with."""

SYS_VERSION: Final = "1.3.6.1.4.1.12356.101.4.1.1.0"
"""FORTINET-FORTIGATE-MIB ``fgSysVersion`` — the running FortiOS build."""

_MODEL: Final = re.compile(r"\b(Forti\w[\w\-]*)")
"""``sysDescr`` on a FortiGate is the product name, e.g. "FortiGate-100F"."""


class FortinetFortiOsAdapter(SnmpVendorAdapter):
    """FortiOS, which puts the serial and the version in its own MIB and the model in sysDescr."""

    vendor: ClassVar[str] = "FortinetFortiOs"

    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.DISCOVER, JobKind.POLL, JobKind.CONFIG_FETCH}
    )

    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ("1.3.6.1.4.1.12356",)

    system_description_markers: ClassVar[tuple[str, ...]] = ("FortiGate", "Fortinet")

    def scalar_oids(self) -> tuple[str, ...]:
        return (SYS_SERIAL, SYS_VERSION)

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
            os_version=text(row, SYS_VERSION) or base.os_version,
            serial_number=text(row, SYS_SERIAL) or base.serial_number,
        )
