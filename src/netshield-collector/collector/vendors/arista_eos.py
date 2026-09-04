"""Arista EOS."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from typing import ClassVar, Final

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.vendors.base import SnmpVendorAdapter

_VERSION: Final = re.compile(r"EOS version\s+(\S+)", re.IGNORECASE)
_MODEL: Final = re.compile(r"running on an?\s+Arista Networks\s+(\S+)", re.IGNORECASE)
"""``sysDescr`` reads "Arista Networks EOS version 4.29.2F running on an Arista Networks
DCS-7050SX3-48YC8"."""


class AristaEosAdapter(SnmpVendorAdapter):
    """EOS, whose ``sysDescr`` carries both the version and the model in a fixed sentence."""

    vendor: ClassVar[str] = "AristaEos"

    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.DISCOVER, JobKind.POLL, JobKind.CONFIG_FETCH}
    )

    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ("1.3.6.1.4.1.30065",)

    system_description_markers: ClassVar[tuple[str, ...]] = ("Arista Networks EOS",)

    def describe(
        self,
        system: SystemGroup,
        scalars: Mapping[str, str],
        entities: Sequence[PhysicalEntity],
    ) -> VendorFacts:
        base = self.from_chassis(entities)
        descr = system.descr or ""
        model = _MODEL.search(descr)
        version = _VERSION.search(descr)

        return VendorFacts(
            model=model.group(1).rstrip(",") if model else base.model,
            os_version=version.group(1).rstrip(",") if version else base.os_version,
            serial_number=base.serial_number,
        )
