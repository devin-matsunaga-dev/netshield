"""Per-vendor knowledge, one module each, behind a common protocol.

CONVENTIONS.md §5: vendor quirks live in ``collector/vendors/{vendor}.py`` behind a common
``VendorAdapter`` protocol, and no vendor ``if`` chain appears in shared code. WP-1.3 defined the
protocol and the registry; WP-1.5 fills in the SNMP members and the seven adapters SPEC.md §4
names.

:func:`snmp_adapters` is the whole list, in the order it does not matter in — the registry
resolves by what the device said, not by registration order.
"""

from collector.vendors.arista_eos import AristaEosAdapter
from collector.vendors.base import GENERIC_SNMP, SnmpVendorAdapter, VendorAdapter, VendorRegistry
from collector.vendors.cisco_ios import CiscoIosAdapter
from collector.vendors.cisco_nxos import CiscoNxOsAdapter
from collector.vendors.fortinet_fortios import FortinetFortiOsAdapter
from collector.vendors.generic_snmp import GenericSnmpAdapter
from collector.vendors.juniper_junos import JuniperJunOsAdapter
from collector.vendors.mikrotik_routeros import MikroTikRouterOsAdapter


def snmp_adapters() -> list[VendorAdapter]:
    """Every adapter SPEC.md §4 names, plus the generic-SNMP fallback."""
    return [
        AristaEosAdapter(),
        CiscoIosAdapter(),
        CiscoNxOsAdapter(),
        FortinetFortiOsAdapter(),
        GenericSnmpAdapter(),
        JuniperJunOsAdapter(),
        MikroTikRouterOsAdapter(),
    ]


__all__ = [
    "GENERIC_SNMP",
    "AristaEosAdapter",
    "CiscoIosAdapter",
    "CiscoNxOsAdapter",
    "FortinetFortiOsAdapter",
    "GenericSnmpAdapter",
    "JuniperJunOsAdapter",
    "MikroTikRouterOsAdapter",
    "SnmpVendorAdapter",
    "VendorAdapter",
    "VendorRegistry",
    "snmp_adapters",
]
