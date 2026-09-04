"""The vendor seam: what the seven adapters declare, and how a device is matched to one.

WP-1.3 shipped this seam with no adapters behind it and a test saying so. WP-1.5 fills it in, so
that test is replaced by these: the adapters exist, they declare what SPEC.md §4 fixes, and the
registry resolves a device from what the device said rather than from a vendor ``if`` chain.
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from typing import ClassVar

import pytest

from collector.jobs import ExecutorRegistry, JobExecutor
from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts
from collector.vendors import (
    AristaEosAdapter,
    CiscoIosAdapter,
    CiscoNxOsAdapter,
    GenericSnmpAdapter,
    JuniperJunOsAdapter,
    VendorAdapter,
    VendorRegistry,
    snmp_adapters,
)
from collector.vendors.base import GENERIC_SNMP

VENDORS = frozenset(
    {
        "AristaEos",
        "CiscoIos",
        "CiscoNxOs",
        "FortinetFortiOs",
        "GenericSnmp",
        "JuniperJunOs",
        "MikroTikRouterOs",
    }
)
"""The seven SPEC.md §4 names, spelled as ``DeviceVendor`` spells them on the API side."""


def registry() -> VendorRegistry:
    return VendorRegistry(snmp_adapters())


def test_every_vendor_spec_names_has_an_adapter_and_no_others_do() -> None:
    assert {adapter.vendor for adapter in snmp_adapters()} == VENDORS


def test_every_adapter_satisfies_the_protocol() -> None:
    assert all(isinstance(adapter, VendorAdapter) for adapter in snmp_adapters())


def test_generic_snmp_is_the_only_adapter_with_a_reduced_feature_set() -> None:
    """SPEC.md §4: the fallback is labelled, and nothing else is."""
    reduced = {adapter.vendor for adapter in snmp_adapters() if adapter.reduced_capability}

    assert reduced == {GENERIC_SNMP}


def test_generic_snmp_cannot_serve_a_config_fetch_and_every_named_vendor_can() -> None:
    """ "No CLI features" is what the reduced feature set actually means."""
    for adapter in snmp_adapters():
        assert JobKind.DISCOVER in adapter.supported_kinds
        assert (JobKind.CONFIG_FETCH in adapter.supported_kinds) is (adapter.vendor != GENERIC_SNMP)


# --- Resolution --------------------------------------------------------------------------------


def system(object_id: str | None = None, descr: str | None = None) -> SystemGroup:
    return SystemGroup(object_id=object_id, descr=descr)


def test_a_sysobjectid_under_a_vendors_arc_resolves_to_that_vendor() -> None:
    resolved = registry().resolve(system(object_id="1.3.6.1.4.1.30065.1.3011.7050"))

    assert resolved is not None
    assert resolved.vendor == "AristaEos"


def test_the_longer_arc_wins_so_a_nexus_is_not_answered_by_the_ios_adapter() -> None:
    """Both are Cisco. 1.3.6.1.4.1.9.1 is IOS and 1.3.6.1.4.1.9.12.3.1.3 is NX-OS."""
    resolved = registry().resolve(system(object_id="1.3.6.1.4.1.9.12.3.1.3.1734"))

    assert resolved is not None
    assert resolved.vendor == "CiscoNxOs"


def test_a_prefix_matches_on_whole_arcs_rather_than_on_characters() -> None:
    """A plain string prefix would have 1.3.6.1.4.1.9.1 claiming every 9.12 OID there is."""
    assert CiscoIosAdapter().object_id_match(system(object_id="1.3.6.1.4.1.9.12.3.1.3.1")) is None
    assert CiscoIosAdapter().object_id_match(system(object_id="1.3.6.1.4.1.9.1.2494")) is not None


def test_a_device_with_no_sysobjectid_is_matched_on_its_description() -> None:
    resolved = registry().resolve(
        system(descr="Arista Networks EOS version 4.29.2F running on an Arista Networks DCS-7050")
    )

    assert resolved is not None
    assert resolved.vendor == "AristaEos"


def test_a_sysobjectid_match_beats_a_description_match() -> None:
    """The arc is the vendor's own assertion; a description is prose that can quote anything."""
    resolved = registry().resolve(
        system(object_id="1.3.6.1.4.1.2636.1.1.1.2.79", descr="pretending to be RouterOS")
    )

    assert resolved is not None
    assert resolved.vendor == "JuniperJunOs"


def test_an_unrecognised_device_falls_back_to_generic_snmp() -> None:
    resolved = registry().resolve(system(object_id="1.3.6.1.4.1.99999.1.2.3", descr="Something"))

    assert resolved is not None
    assert resolved.vendor == GENERIC_SNMP
    assert resolved.reduced_capability


def test_resolution_does_not_depend_on_registration_order() -> None:
    forwards = VendorRegistry(snmp_adapters())
    backwards = VendorRegistry(list(reversed(snmp_adapters())))
    described = system(descr="Cisco IOS Software, Version 15.2(7)E3")

    assert forwards.resolve(described) is not None
    assert backwards.resolve(described) is not None
    assert forwards.resolve(described).vendor == backwards.resolve(described).vendor  # type: ignore[union-attr]


def test_with_no_adapters_at_all_resolution_answers_nothing_rather_than_raising() -> None:
    """The runner has to be able to fail a job rather than raise out of the loop."""
    assert VendorRegistry().resolve(system(object_id="1.3.6.1.4.1.9.1.1")) is None


def test_the_generic_adapter_matches_nothing_of_its_own() -> None:
    """It is reached by falling back, never by recognising anything."""
    assert not GenericSnmpAdapter().matches(system(object_id="1.3.6.1.4.1.9.1.1"))
    assert not GenericSnmpAdapter().matches(system(descr="anything at all"))


def test_lookup_by_vendor_name_still_falls_back_to_generic() -> None:
    assert registry().for_vendor("CiscoIos").vendor == "CiscoIos"  # type: ignore[union-attr]
    assert registry().for_vendor("SomethingElse").vendor == GENERIC_SNMP  # type: ignore[union-attr]


def test_registering_a_vendor_twice_is_refused() -> None:
    with pytest.raises(ValueError, match="already registered"):
        registry().register(CiscoIosAdapter())


def test_an_adapter_that_is_not_built_on_the_shared_base_still_gets_to_answer() -> None:
    """The protocol is the contract; SnmpVendorAdapter is only the shared implementation of it."""

    class BespokeAdapter:
        vendor: ClassVar[str] = "Bespoke"
        supported_kinds: ClassVar[frozenset[JobKind]] = frozenset({JobKind.DISCOVER})
        reduced_capability: ClassVar[bool] = False
        system_object_id_prefixes: ClassVar[tuple[str, ...]] = ()
        system_description_markers: ClassVar[tuple[str, ...]] = ()

        def matches(self, system: SystemGroup) -> bool:
            return system.descr == "a device only this adapter knows"

        def scalar_oids(self) -> tuple[str, ...]:
            return ()

        def describe(
            self,
            system: SystemGroup,
            scalars: Mapping[str, str],
            entities: Sequence[PhysicalEntity],
        ) -> VendorFacts:
            raise NotImplementedError

    resolved = VendorRegistry([BespokeAdapter()]).resolve(
        system(descr="a device only this adapter knows")
    )

    assert resolved is not None
    assert resolved.vendor == "Bespoke"


def test_a_named_vendor_beats_a_bespoke_description_match_on_the_arc() -> None:
    combined = VendorRegistry([CiscoNxOsAdapter(), JuniperJunOsAdapter(), AristaEosAdapter()])
    resolved = combined.resolve(system(object_id="1.3.6.1.4.1.2636.1.1.1.2.79", descr="NX-OS"))

    assert resolved is not None
    assert resolved.vendor == "JuniperJunOs"


# --- The executor registry, unchanged since WP-1.3 ---------------------------------------------


class FakeExecutor:
    kind = JobKind.POLL

    async def execute(self, job: object) -> dict[str, object]:
        return {}


def test_an_executor_registry_resolves_by_kind() -> None:
    registry = ExecutorRegistry([FakeExecutor()])

    assert isinstance(registry.for_kind(JobKind.POLL), JobExecutor)
    assert registry.for_kind(JobKind.CONFIG_FETCH) is None


def test_registering_a_kind_twice_is_refused() -> None:
    registry = ExecutorRegistry([FakeExecutor()])

    with pytest.raises(ValueError, match="already registered"):
        registry.register(FakeExecutor())
