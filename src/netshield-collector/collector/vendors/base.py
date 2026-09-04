"""The vendor seam: what every vendor module must declare, how one is found, and how a device is
matched to one.

WP-1.3 defined this protocol with only the two members SPEC.md §4 already fixed — the vendor an
adapter speaks for, and the job kinds it supports — and deliberately declared no SNMP members,
because "the shape of those belongs to WP-1.5 and a signature invented before the protocol
library is chosen is one the first implementer changes". WP-1.5 is that package, and the members
it adds are the ones an SNMP walk actually needs and no more.

What the registry buys is still the rule rather than the routing: shared code asks it which
adapter answers for a device and never branches on a vendor name, so the ``if`` chain
CONVENTIONS.md §5 forbids has nowhere to start growing. Resolution is a loop over the registered
adapters asking each whether it recognises what came back.

**Read only, permanently.** An adapter names OIDs to read and parses what comes back. There is no
member on this protocol that could write to a device, and adding one is forbidden by
ARCHITECTURE.md §1 rather than merely discouraged.
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from typing import ClassVar, Protocol, runtime_checkable

from collector.models import JobKind
from collector.snmp.facts import PhysicalEntity, SystemGroup, VendorFacts, chassis

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

    reduced_capability: ClassVar[bool]
    """
    Whether a device answering to this adapter has a reduced feature set.

    True for generic SNMP alone. It travels back to the API on the walk result and is stored, so
    that the label SPEC.md §4 requires in the UI is a fact recorded at fingerprint time rather
    than something a screen infers from the vendor name.
    """

    system_object_id_prefixes: ClassVar[tuple[str, ...]]
    """``sysObjectID`` prefixes that identify this platform. The primary match."""

    system_description_markers: ClassVar[tuple[str, ...]]
    """``sysDescr`` substrings, matched case-insensitively. The fallback match."""

    def matches(self, system: SystemGroup) -> bool:
        """Whether this adapter recognises the device the system group describes."""
        ...

    def scalar_oids(self) -> tuple[str, ...]:
        """Vendor-private scalars to read in addition to the standard ones. May be empty."""
        ...

    def describe(
        self,
        system: SystemGroup,
        scalars: Mapping[str, str],
        entities: Sequence[PhysicalEntity],
    ) -> VendorFacts:
        """Model, OS version and serial, from whichever of the three sources this vendor uses."""
        ...


class SnmpVendorAdapter:
    """The behaviour every vendor shares, so a vendor module holds only its own quirks.

    Matching is identical for all of them — a ``sysObjectID`` prefix, or a ``sysDescr`` marker —
    and so is the last-resort answer, which is ENTITY-MIB's chassis row. A subclass overrides
    :meth:`describe` when its vendor puts the same three facts somewhere better, and overrides
    :meth:`scalar_oids` when reaching them needs a private OID.
    """

    vendor: ClassVar[str] = GENERIC_SNMP
    supported_kinds: ClassVar[frozenset[JobKind]] = frozenset({JobKind.DISCOVER})
    reduced_capability: ClassVar[bool] = False
    system_object_id_prefixes: ClassVar[tuple[str, ...]] = ()
    system_description_markers: ClassVar[tuple[str, ...]] = ()

    def matches(self, system: SystemGroup) -> bool:
        return self.object_id_match(system) is not None or self.description_matches(system)

    def object_id_match(self, system: SystemGroup) -> str | None:
        """The longest declared prefix that ``sysObjectID`` sits under, or nothing.

        Matching is on whole arcs: ``1.3.6.1.4.1.9.1`` must not claim ``1.3.6.1.4.1.912``, which
        a plain string prefix would.
        """
        object_id = system.object_id

        if not object_id:
            return None

        matched = [
            prefix
            for prefix in self.system_object_id_prefixes
            if object_id == prefix or object_id.startswith(f"{prefix}.")
        ]

        return max(matched, key=len) if matched else None

    def description_matches(self, system: SystemGroup) -> bool:
        """Whether ``sysDescr`` carries one of this vendor's markers."""
        descr = (system.descr or "").casefold()

        return any(marker.casefold() in descr for marker in self.system_description_markers)

    def scalar_oids(self) -> tuple[str, ...]:
        return ()

    def describe(
        self,
        system: SystemGroup,
        scalars: Mapping[str, str],
        entities: Sequence[PhysicalEntity],
    ) -> VendorFacts:
        return self.from_chassis(entities)

    @staticmethod
    def from_chassis(entities: Sequence[PhysicalEntity]) -> VendorFacts:
        """What ENTITY-MIB says about the box, which is all a device without a private MIB gives.

        ``entPhysicalModelName`` is the vendor's own product name and is preferred for the model;
        ``entPhysicalDescr`` stands in where a device fills one and not the other.
        """
        box = chassis(entities)

        if box is None:
            return VendorFacts()

        return VendorFacts(
            model=box.model_name or box.descr,
            os_version=box.software_revision,
            serial_number=box.serial_number,
        )


class VendorRegistry:
    """Finds the adapter for a device, from what the device said about itself."""

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
        """The adapter registered for ``vendor``, falling back to generic SNMP.

        ``None`` is a real answer rather than an error: with no adapters registered at all the
        runner must be able to fail a job with a reason instead of raising out of the loop that
        was meant to keep running.
        """
        return self._adapters.get(vendor) or self._adapters.get(GENERIC_SNMP)

    def resolve(self, system: SystemGroup) -> VendorAdapter | None:
        """Which adapter answers for the device this system group describes.

        A ``sysObjectID`` match beats a ``sysDescr`` match, and the longest prefix beats a
        shorter one — which is what keeps Cisco's NX-OS arc, ``1.3.6.1.4.1.9.12.3.1.3``, from
        being answered by an adapter that had claimed all of ``1.3.6.1.4.1.9``. Among equals the
        vendor name orders them, so the answer does not depend on registration order.

        Falls back to generic SNMP, which is SPEC.md §4's rule stated once.
        """
        best: tuple[int, str, VendorAdapter] | None = None

        for adapter in self._ordered():
            if not isinstance(adapter, SnmpVendorAdapter):
                # An adapter that is not built on the shared base still gets to answer; it just
                # cannot express "how well" it matched, so it ranks below any prefix match.
                if adapter.matches(system) and best is None:
                    best = (0, adapter.vendor, adapter)

                continue

            prefix = adapter.object_id_match(system)

            if prefix is not None:
                candidate = (len(prefix.split(".")), adapter.vendor, adapter)

                if best is None or candidate[0] > best[0]:
                    best = candidate
            elif best is None and adapter.description_matches(system):
                best = (0, adapter.vendor, adapter)

        return best[2] if best is not None else self._adapters.get(GENERIC_SNMP)

    def _ordered(self) -> list[VendorAdapter]:
        return [self._adapters[vendor] for vendor in sorted(self._adapters)]

    def __len__(self) -> int:
        return len(self._adapters)
