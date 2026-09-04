"""The vendor seam. There are no adapters yet; what is checked is that the seam behaves."""

from __future__ import annotations

from typing import ClassVar

import pytest

from collector.jobs import ExecutorRegistry, JobExecutor
from collector.models import JobKind
from collector.vendors import VendorAdapter, VendorRegistry
from collector.vendors.base import GENERIC_SNMP


class FakeAdapter:
    vendor: ClassVar[str] = "CiscoIos"
    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset(
        {JobKind.POLL, JobKind.DISCOVER, JobKind.CONFIG_FETCH}
    )


class FakeGenericAdapter:
    vendor: ClassVar[str] = GENERIC_SNMP
    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset({JobKind.POLL, JobKind.DISCOVER})


def test_the_protocol_is_satisfied_by_a_declaration_alone() -> None:
    assert isinstance(FakeAdapter(), VendorAdapter)


def test_no_adapters_are_shipped_in_this_package() -> None:
    """WP-1.3: the protocol is defined and no vendor implements it yet."""
    assert len(VendorRegistry()) == 0


def test_an_unknown_vendor_falls_back_to_generic_snmp() -> None:
    """SPEC.md §4: anything unrecognised lands as generic SNMP with a reduced feature set."""
    registry = VendorRegistry([FakeAdapter(), FakeGenericAdapter()])

    assert registry.for_vendor("CiscoIos").vendor == "CiscoIos"  # type: ignore[union-attr]
    assert registry.for_vendor("SomethingElse").vendor == GENERIC_SNMP  # type: ignore[union-attr]


def test_with_no_generic_adapter_an_unknown_vendor_resolves_to_nothing() -> None:
    """The runner has to be able to fail a job rather than raise out of the loop."""
    assert VendorRegistry([FakeAdapter()]).for_vendor("SomethingElse") is None


def test_registering_a_vendor_twice_is_refused() -> None:
    registry = VendorRegistry([FakeAdapter()])

    with pytest.raises(ValueError, match="already registered"):
        registry.register(FakeAdapter())


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
