"""The vendor seam: what every vendor module must declare, and how one is found.

There are no adapters yet, and that is the whole of WP-1.3's instruction — "``VendorAdapter``
protocol defined with no vendor implementations yet". What that leaves is a genuine question
about how much of the protocol can honestly be written now.

The answer taken here is: only what is already settled. A vendor adapter declares which vendor it
speaks for and which job kinds that vendor supports, because SPEC.md §4 already fixes both — the
seven supported vendors, and generic SNMP as a fallback with a reduced feature set that the UI
must label. It declares no SNMP or SSH members, because the shape of those belongs to WP-1.5 and
Phase 7, and a method signature invented before the protocol library is chosen is a signature
that gets changed by the first package that tries to implement it.

What the registry buys today is the rule, not the routing: shared code asks it for an adapter and
never branches on a vendor name, so the ``if`` chain CONVENTIONS.md §5 forbids has nowhere to
start growing.
"""

from __future__ import annotations

from typing import ClassVar, Protocol, runtime_checkable

from collector.models import JobKind

GENERIC_SNMP: str = "GenericSnmp"
"""The fallback vendor.

SPEC.md §4: anything NetShield does not recognise falls back to generic SNMP with a clearly
labelled reduced feature set. It is named here so the registry's fallback is a constant rather
than a string repeated at each call site.
"""


@runtime_checkable
class VendorAdapter(Protocol):
    """What one vendor's module must declare."""

    vendor: ClassVar[str]
    """The ``DeviceVendor`` member this adapter speaks for, spelled as the API spells it."""

    supported_kinds: ClassVar[frozenset[JobKind]]
    """
    The job kinds this vendor can serve.

    Generic SNMP supports fewer than the rest, which is exactly the "reduced feature set"
    SPEC.md §4 requires the platform to be able to say out loud.
    """


class VendorRegistry:
    """Finds the adapter for a device's vendor, or the generic-SNMP fallback."""

    def __init__(self, adapters: list[VendorAdapter] | None = None) -> None:
        self._adapters: dict[str, VendorAdapter] = {
            adapter.vendor: adapter for adapter in (adapters or [])
        }

    def register(self, adapter: VendorAdapter) -> None:
        """Adds one vendor's adapter. Registering a vendor twice is a mistake, not a merge."""
        if adapter.vendor in self._adapters:
            raise ValueError(f"An adapter for {adapter.vendor} is already registered.")

        self._adapters[adapter.vendor] = adapter

    def for_vendor(self, vendor: str) -> VendorAdapter | None:
        """The adapter for ``vendor``, falling back to generic SNMP, or ``None`` if neither is
        registered.

        ``None`` is a real answer rather than an error: with no adapters registered at all — which
        is every deployment of WP-1.3 — the runner must be able to fail a job with a reason
        instead of raising out of the loop that was meant to keep running.
        """
        return self._adapters.get(vendor) or self._adapters.get(GENERIC_SNMP)

    def __len__(self) -> int:
        return len(self._adapters)
