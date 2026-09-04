"""Generic SNMP: the fallback for a device NetShield does not recognise.

SPEC.md §4 — anything outside the six named platforms falls back to generic SNMP "with a
clearly-labeled reduced feature set in the UI". This adapter is what makes that label a recorded
fact rather than an inference: it is the only adapter whose ``reduced_capability`` is true, and
the flag travels back on the walk result and is stored against the device.

It matches nothing. The registry falls back to it when no other adapter recognised the device,
which is the only way a device arrives here.
"""

from __future__ import annotations

from typing import ClassVar

from collector.models import JobKind
from collector.vendors.base import GENERIC_SNMP, SnmpVendorAdapter


class GenericSnmpAdapter(SnmpVendorAdapter):
    """Whatever the standard MIBs say, and nothing more."""

    vendor: ClassVar[str] = GENERIC_SNMP

    # No ConfigFetch. That is the CLI-shaped kind, and "no CLI features" is precisely what
    # SPEC.md §4 says a generic-SNMP device has — so the reduced feature set is expressed here as
    # a kind this vendor cannot serve, rather than only as a boolean.
    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset({JobKind.DISCOVER, JobKind.POLL})

    reduced_capability: ClassVar[bool] = True
