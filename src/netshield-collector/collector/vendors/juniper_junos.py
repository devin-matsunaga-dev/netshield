"""Juniper JunOS."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from typing import ClassVar, Final

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.snmp.tables import text
from collector.vendors.base import SnmpVendorAdapter

BOX_DESCR: Final = "1.3.6.1.4.1.2636.3.1.2.0"
"""JUNIPER-MIB ``jnxBoxDescr`` — the chassis as Juniper names it."""

BOX_SERIAL: Final = "1.3.6.1.4.1.2636.3.1.3.0"
"""JUNIPER-MIB ``jnxBoxSerialNo``."""

_VERSION: Final = re.compile(r"(?:JUNOS|Junos:)\s*([^\s,\[\]]+)")
"""``sysDescr`` reads "… Ethernet Switch, kernel JUNOS 21.4R3.15, Build date: …"."""

_MODEL: Final = re.compile(r"Juniper Networks,\s*Inc\.\s+(\S+)")
"""The product token Juniper puts immediately after its own name."""


class JuniperJunOsAdapter(SnmpVendorAdapter):
    """JunOS, which answers the three facts out of its own MIB rather than ENTITY-MIB."""

    vendor: ClassVar[str] = "JuniperJunOs"

    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.DISCOVER, JobKind.POLL, JobKind.CONFIG_FETCH}
    )

    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ("1.3.6.1.4.1.2636",)

    system_description_markers: ClassVar[tuple[str, ...]] = ("JUNOS", "Juniper Networks")

    def scalar_oids(self) -> tuple[str, ...]:
        return (BOX_DESCR, BOX_SERIAL)

    def describe(
        self,
        system: SystemGroup,
        scalars: Mapping[str, str],
        entities: Sequence[PhysicalEntity],
    ) -> VendorFacts:
        base = self.from_chassis(entities)
        row = dict(scalars)
        descr = system.descr or ""
        model = _MODEL.search(descr)
        version = _VERSION.search(descr)

        return VendorFacts(
            model=text(row, BOX_DESCR) or (model.group(1) if model else base.model),
            os_version=version.group(1).rstrip(",") if version else base.os_version,
            serial_number=text(row, BOX_SERIAL) or base.serial_number,
        )
