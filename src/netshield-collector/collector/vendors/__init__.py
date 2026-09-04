"""Per-vendor knowledge, one module each, behind a common protocol.

CONVENTIONS.md §5: vendor quirks live in ``collector/vendors/{vendor}.py`` behind a common
``VendorAdapter`` protocol, and no vendor ``if`` chain appears in shared code. WP-1.3 defines the
protocol and the registry; the seven adapters SPEC.md §4 names arrive with the packages that can
actually exercise them.
"""

from collector.vendors.base import VendorAdapter, VendorRegistry

__all__ = ["VendorAdapter", "VendorRegistry"]
