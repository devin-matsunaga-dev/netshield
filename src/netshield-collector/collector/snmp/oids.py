"""The object identifiers NetShield reads, as literal dotted strings.

**Numeric OIDs only, and no MIB modules.** ``pysnmp`` can compile and load MIB source, and where
it cannot find a module locally it will go and fetch one — which is an outbound internet call at
runtime and therefore forbidden outright (ARCHITECTURE.md §1). Writing every object as its number
means nothing is ever resolved by name, so there is nothing for it to fetch. ``pysmi``, the
compiler that would do the fetching, is deliberately absent from this project's dependencies.

The cost is that the names live in comments rather than in a MIB, so each constant is written
next to the object it stands for. That is a fair trade for a fixed, small set of objects that
have not moved since RFC 1213.

Every OID here is read. There is no ``set`` in this file, in this package, or anywhere in
NetShield, and there never will be (SPEC.md §3).
"""

from __future__ import annotations

from typing import Final

# --- SNMPv2-MIB, the system group (RFC 3418 §2). Scalars, so each carries its .0 instance. ---

SYS_DESCR: Final = "1.3.6.1.2.1.1.1.0"
"""``sysDescr`` — the free-text description a device gives of itself."""

SYS_OBJECT_ID: Final = "1.3.6.1.2.1.1.2.0"
"""``sysObjectID`` — the vendor's own identifier for this model. The primary fingerprint."""

SYS_UP_TIME: Final = "1.3.6.1.2.1.1.3.0"
"""``sysUpTime`` — hundredths of a second since the network management stack came up.

A 32-bit counter, so it wraps after roughly 497 days. What is read here is what the agent said;
nothing tries to reconstruct a boot time from it.
"""

SYS_CONTACT: Final = "1.3.6.1.2.1.1.4.0"
"""``sysContact``."""

SYS_NAME: Final = "1.3.6.1.2.1.1.5.0"
"""``sysName`` — the device's own idea of its name, which need not be its NetShield hostname."""

SYS_LOCATION: Final = "1.3.6.1.2.1.1.6.0"
"""``sysLocation``."""

SYSTEM_SCALARS: Final = (
    SYS_DESCR,
    SYS_OBJECT_ID,
    SYS_UP_TIME,
    SYS_CONTACT,
    SYS_NAME,
    SYS_LOCATION,
)
"""The one request that decides everything else: which adapter answers for this device."""

# --- IF-MIB, ifTable (RFC 2863). Columns under 1.3.6.1.2.1.2.2.1, indexed by ifIndex. ---

IF_TABLE: Final = "1.3.6.1.2.1.2.2.1"
"""``ifEntry`` — the subtree walked for the interface inventory."""

IF_INDEX: Final = "1.3.6.1.2.1.2.2.1.1"
IF_DESCR: Final = "1.3.6.1.2.1.2.2.1.2"
IF_TYPE: Final = "1.3.6.1.2.1.2.2.1.3"
IF_MTU: Final = "1.3.6.1.2.1.2.2.1.4"
IF_SPEED: Final = "1.3.6.1.2.1.2.2.1.5"
IF_PHYS_ADDRESS: Final = "1.3.6.1.2.1.2.2.1.6"
IF_ADMIN_STATUS: Final = "1.3.6.1.2.1.2.2.1.7"
IF_OPER_STATUS: Final = "1.3.6.1.2.1.2.2.1.8"

# --- IF-MIB, ifXTable (RFC 2863). The 64-bit and named-interface extensions. ---

IF_X_TABLE: Final = "1.3.6.1.2.1.31.1.1.1"
"""``ifXEntry`` — same index as ifTable, walked for the columns ifTable predates."""

IF_NAME: Final = "1.3.6.1.2.1.31.1.1.1.1"
IF_HIGH_SPEED: Final = "1.3.6.1.2.1.31.1.1.1.15"
"""``ifHighSpeed`` — megabits per second. Correct above the 4.29 Gbit/s ceiling of ``ifSpeed``."""

IF_ALIAS: Final = "1.3.6.1.2.1.31.1.1.1.18"
"""``ifAlias`` — the interface description an operator configured."""

# --- ENTITY-MIB, entPhysicalTable (RFC 6933). Columns under 1.3.6.1.2.1.47.1.1.1.1. ---

ENT_PHYSICAL_TABLE: Final = "1.3.6.1.2.1.47.1.1.1.1"
"""``entPhysicalEntry`` — where a serial and a model live on every vendor that implements it."""

ENT_PHYSICAL_DESCR: Final = "1.3.6.1.2.1.47.1.1.1.1.2"
ENT_PHYSICAL_CLASS: Final = "1.3.6.1.2.1.47.1.1.1.1.5"
ENT_PHYSICAL_NAME: Final = "1.3.6.1.2.1.47.1.1.1.1.7"
ENT_PHYSICAL_HARDWARE_REV: Final = "1.3.6.1.2.1.47.1.1.1.1.8"
ENT_PHYSICAL_FIRMWARE_REV: Final = "1.3.6.1.2.1.47.1.1.1.1.9"
ENT_PHYSICAL_SOFTWARE_REV: Final = "1.3.6.1.2.1.47.1.1.1.1.10"
ENT_PHYSICAL_SERIAL_NUM: Final = "1.3.6.1.2.1.47.1.1.1.1.11"
ENT_PHYSICAL_MODEL_NAME: Final = "1.3.6.1.2.1.47.1.1.1.1.13"

ENT_PHYSICAL_CLASS_CHASSIS: Final = 3
"""``PhysicalClass.chassis``. The entry that describes the box itself rather than a part of it."""

# --- IP-MIB, ipNetToPhysicalTable (RFC 4293). Columns under 1.3.6.1.2.1.4.35.1. ---

IP_NET_TO_PHYSICAL_TABLE: Final = "1.3.6.1.2.1.4.35.1"
"""``ipNetToPhysicalEntry`` — the modern neighbour cache. IPv4 and IPv6 in one table.

The index is ``ifIndex.addressType.addressLength.address...``, which is why
:func:`collector.snmp.tables.rows` keeps an index as the dotted string that followed the column
rather than parsing it as one integer: an entry for 10.0.0.1 on interface 3 is indexed
``3.1.4.10.0.0.1``, and for an IPv6 address it is nineteen sub-identifiers long.
"""

IP_NET_TO_PHYSICAL_PHYS_ADDRESS: Final = "1.3.6.1.2.1.4.35.1.4"
"""``ipNetToPhysicalPhysAddress`` — the hardware address the entry binds to."""

IP_NET_TO_PHYSICAL_TYPE: Final = "1.3.6.1.2.1.4.35.1.7"
"""``ipNetToPhysicalType`` — other(1), invalid(2), dynamic(3), static(4), local(5)."""

IP_NET_TO_PHYSICAL_STATE: Final = "1.3.6.1.2.1.4.35.1.8"
"""``ipNetToPhysicalState`` — reachable(1) … unknown(6). An entry in ``incomplete(5)`` is the
device saying it asked and got no answer, which is not a client."""

IP_NET_TO_PHYSICAL_TYPE_INVALID: Final = 2
"""``invalid``. The row is being removed and binds nothing."""

IP_NET_TO_PHYSICAL_STATE_INCOMPLETE: Final = 5
"""``incomplete``. Address resolution is in progress and has resolved nothing."""

ADDRESS_TYPE_IPV4: Final = 1
ADDRESS_TYPE_IPV6: Final = 2
"""``InetAddressType`` from INET-ADDRESS-MIB, as it appears in the index."""

# --- RFC 1213 / IP-MIB, ipNetToMediaTable. Columns under 1.3.6.1.2.1.4.22.1. ---

IP_NET_TO_MEDIA_TABLE: Final = "1.3.6.1.2.1.4.22.1"
"""``ipNetToMediaEntry`` — the older, IPv4-only ARP table.

Deprecated by RFC 4293 and still the only one plenty of agents answer, so it is the fallback
rather than the first choice. Its index is ``ifIndex.a.b.c.d``, which is five sub-identifiers and
always IPv4.
"""

IP_NET_TO_MEDIA_PHYS_ADDRESS: Final = "1.3.6.1.2.1.4.22.1.2"
IP_NET_TO_MEDIA_NET_ADDRESS: Final = "1.3.6.1.2.1.4.22.1.3"
IP_NET_TO_MEDIA_TYPE: Final = "1.3.6.1.2.1.4.22.1.4"
"""``ipNetToMediaType`` — other(1), invalid(2), dynamic(3), static(4)."""

IP_NET_TO_MEDIA_TYPE_INVALID: Final = 2

# --- Q-BRIDGE-MIB, dot1qTpFdbTable (RFC 4363). Columns under 1.3.6.1.2.1.17.7.1.2.2.1. ---

DOT1Q_TP_FDB_TABLE: Final = "1.3.6.1.2.1.17.7.1.2.2.1"
"""``dot1qTpFdbEntry`` — the VLAN-aware forwarding database.

Preferred over the older ``dot1dTpFdbTable`` because it is indexed by
``dot1qFdbId.macAddress``, so one walk covers every VLAN and each entry says which VLAN it was
learned on. The VLAN-unaware table has to be walked once per VLAN through a community string
suffixed ``@vlan`` on some platforms, which is a per-vendor trick this collector does not do.
"""

DOT1Q_TP_FDB_PORT: Final = "1.3.6.1.2.1.17.7.1.2.2.1.2"
"""``dot1qTpFdbPort`` — the *bridge port* number, not the ``ifIndex``."""

DOT1Q_TP_FDB_STATUS: Final = "1.3.6.1.2.1.17.7.1.2.2.1.3"
"""``dot1qTpFdbStatus`` — other(1), invalid(2), learned(3), self(4), mgmt(5)."""

DOT1Q_TP_FDB_STATUS_INVALID: Final = 2
DOT1Q_TP_FDB_STATUS_SELF: Final = 4
"""``self``. The address is the bridge's own, not something attached to the port."""

# --- BRIDGE-MIB, dot1dTpFdbTable (RFC 4188). Columns under 1.3.6.1.2.1.17.4.3.1. ---

DOT1D_TP_FDB_TABLE: Final = "1.3.6.1.2.1.17.4.3.1"
"""``dot1dTpFdbEntry`` — the VLAN-unaware forwarding database, indexed by MAC alone."""

DOT1D_TP_FDB_PORT: Final = "1.3.6.1.2.1.17.4.3.1.2"
DOT1D_TP_FDB_STATUS: Final = "1.3.6.1.2.1.17.4.3.1.3"
"""``dot1dTpFdbStatus`` — other(1), invalid(2), learned(3), self(4), mgmt(5)."""

DOT1D_TP_FDB_STATUS_INVALID: Final = 2
DOT1D_TP_FDB_STATUS_SELF: Final = 4

# --- BRIDGE-MIB, dot1dBasePortTable. Columns under 1.3.6.1.2.1.17.1.4.1. ---

DOT1D_BASE_PORT_TABLE: Final = "1.3.6.1.2.1.17.1.4.1"
"""``dot1dBasePortEntry`` — how a bridge port number becomes an ``ifIndex``.

Both forwarding tables are keyed by a bridge port, which is a number local to the bridge and is
*not* the interface index the rest of NetShield speaks. Without this join a forwarding entry names
a port nothing else in the inventory can identify.
"""

DOT1D_BASE_PORT_IF_INDEX: Final = "1.3.6.1.2.1.17.1.4.1.2"
"""``dot1dBasePortIfIndex`` — the ``ifIndex`` a bridge port belongs to."""

# --- LLDP-MIB (IEEE 802.1AB). The one neighbour protocol every vendor in SPEC.md §4 speaks. ---

LLDP_LOC_CHASSIS_ID_SUBTYPE: Final = "1.0.8802.1.1.2.1.3.1.0"
"""``lldpLocChassisIdSubtype`` — how this device identifies itself."""

LLDP_LOC_CHASSIS_ID: Final = "1.0.8802.1.1.2.1.3.2.0"
"""``lldpLocChassisId`` — the identity this device advertises, usually its base MAC address."""

LLDP_LOC_SYS_NAME: Final = "1.0.8802.1.1.2.1.3.3.0"
"""``lldpLocSysName`` — the name this device advertises."""

LLDP_LOCAL_SCALARS: Final = (
    LLDP_LOC_CHASSIS_ID_SUBTYPE,
    LLDP_LOC_CHASSIS_ID,
    LLDP_LOC_SYS_NAME,
)
"""What a device says about *itself* over LLDP.

Read because it is the other half of every edge: a neighbour's ``lldpRemChassisId`` is matched
against this value on the device that advertised it, which is how two ends of one cable are
recognised as one link rather than as two unrelated observations.
"""

LLDP_LOC_PORT_TABLE: Final = "1.0.8802.1.1.2.1.3.7.1"
"""``lldpLocPortEntry`` — the local ports LLDP runs on, indexed by ``lldpLocPortNum``.

``lldpLocPortNum`` is *not* an ``ifIndex``. IEEE 802.1AB says only that it is locally unique and
stable across reinitialisations; some vendors happen to use the interface index and others use a
dense sequence of their own. The join back to an ``ifIndex`` is therefore by name — see
:func:`collector.snmp.lldp.resolve_local_ports`.
"""

LLDP_LOC_PORT_ID_SUBTYPE: Final = "1.0.8802.1.1.2.1.3.7.1.2"
LLDP_LOC_PORT_ID: Final = "1.0.8802.1.1.2.1.3.7.1.3"
LLDP_LOC_PORT_DESC: Final = "1.0.8802.1.1.2.1.3.7.1.4"

LLDP_REM_TABLE: Final = "1.0.8802.1.1.2.1.4.1.1"
"""``lldpRemEntry`` — what this device has heard from its neighbours.

Indexed by ``lldpRemTimeMark.lldpRemLocalPortNum.lldpRemIndex``: three sub-identifiers, of which
only the middle one identifies a port. The time mark changes whenever the entry is refreshed, so
an index is not stable and nothing may be keyed by it.
"""

LLDP_REM_CHASSIS_ID_SUBTYPE: Final = "1.0.8802.1.1.2.1.4.1.1.4"
LLDP_REM_CHASSIS_ID: Final = "1.0.8802.1.1.2.1.4.1.1.5"
LLDP_REM_PORT_ID_SUBTYPE: Final = "1.0.8802.1.1.2.1.4.1.1.6"
LLDP_REM_PORT_ID: Final = "1.0.8802.1.1.2.1.4.1.1.7"
LLDP_REM_PORT_DESC: Final = "1.0.8802.1.1.2.1.4.1.1.8"
LLDP_REM_SYS_NAME: Final = "1.0.8802.1.1.2.1.4.1.1.9"
LLDP_REM_SYS_DESC: Final = "1.0.8802.1.1.2.1.4.1.1.10"
LLDP_REM_SYS_CAP_ENABLED: Final = "1.0.8802.1.1.2.1.4.1.1.12"

LLDP_REM_MAN_ADDR_TABLE: Final = "1.0.8802.1.1.2.1.4.2.1"
"""``lldpRemManAddrEntry`` — a neighbour's management addresses.

The address is in the index, not in a column:
``lldpRemTimeMark.lldpRemLocalPortNum.lldpRemIndex.addressSubtype.addressLength.address…``. It is
what turns a neighbour into a device NetShield already knows: an address that matches a device's
``primary_ip_address`` identifies the far end of the cable.
"""

LLDP_REM_MAN_ADDR_IF_ID: Final = "1.0.8802.1.1.2.1.4.2.1.4"
"""``lldpRemManAddrIfId`` — read only so that the walk sees the table's rows at all."""

# ``LldpChassisIdSubtype`` and ``LldpPortIdSubtype`` (IEEE 802.1AB). The two enumerations share
# no numbering, which is why they are separate tables here rather than one shared mapping.
LLDP_CHASSIS_ID_SUBTYPES: Final = {
    1: "ChassisComponent",
    2: "InterfaceAlias",
    3: "PortComponent",
    4: "MacAddress",
    5: "NetworkAddress",
    6: "InterfaceName",
    7: "Local",
}

LLDP_PORT_ID_SUBTYPES: Final = {
    1: "InterfaceAlias",
    2: "PortComponent",
    3: "MacAddress",
    4: "NetworkAddress",
    5: "InterfaceName",
    6: "AgentCircuitId",
    7: "Local",
}

LLDP_ID_KIND_UNKNOWN: Final = "Unknown"
"""What an absent or unrecognised subtype is reported as. The API's enum has the same member."""

LLDP_INTERFACE_NAME_SUBTYPES: Final = frozenset({"InterfaceName", "InterfaceAlias"})
"""The subtypes whose value is an interface name, and can therefore be joined to an ``ifIndex``."""

# --- CISCO-CDP-MIB, cdpCacheTable. Columns under 1.3.6.1.4.1.9.9.23.1.2.1.1. ---

CDP_CACHE_TABLE: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1"
"""``cdpCacheEntry`` — what a Cisco device has heard over CDP.

Indexed by ``cdpCacheIfIndex.cdpCacheDeviceIndex``, so unlike LLDP the local interface index is
in the index itself and needs no join.

CDP is Cisco's own protocol and this is Cisco's own MIB, which is why only the two Cisco adapters
declare it (CONVENTIONS.md §5: the vendor decides, shared code does not branch on a name).
"""

CDP_CACHE_ADDRESS_TYPE: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.3"
CDP_CACHE_ADDRESS: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.4"
"""``cdpCacheAddress`` — the neighbour's address as raw octets, not as text."""

CDP_CACHE_VERSION: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.5"
CDP_CACHE_DEVICE_ID: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.6"
"""``cdpCacheDeviceId`` — usually the neighbour's host name, which WP-1.1 settled is not an
identity. That is the whole reason LLDP outranks CDP where the two disagree."""

CDP_CACHE_DEVICE_PORT: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.7"
CDP_CACHE_PLATFORM: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.8"
CDP_CACHE_CAPABILITIES: Final = "1.3.6.1.4.1.9.9.23.1.2.1.1.9"

CDP_ADDRESS_TYPE_IP: Final = 1
CDP_ADDRESS_TYPE_IPV6: Final = 20
"""``NetworkProtocol`` from CISCO-TC, for the two families NetShield tracks."""

# --- IP-FORWARD-MIB, inetCidrRouteTable (RFC 4292). Columns under 1.3.6.1.2.1.4.24.7.1. ---

INET_CIDR_ROUTE_TABLE: Final = "1.3.6.1.2.1.4.24.7.1"
"""``inetCidrRouteEntry`` — the modern routing table, covering IPv4 and IPv6 in one walk.

The next hop is in the index rather than in a column, and the index is long: destination type,
length-prefixed destination, prefix length, length-prefixed policy OID, next-hop type,
length-prefixed next hop.
"""

INET_CIDR_ROUTE_IF_INDEX: Final = "1.3.6.1.2.1.4.24.7.1.7"
INET_CIDR_ROUTE_TYPE: Final = "1.3.6.1.2.1.4.24.7.1.8"
"""``inetCidrRouteType`` — other(1), reject(2), local(3), remote(4)."""

INET_CIDR_ROUTE_TYPE_REMOTE: Final = 4

# --- IP-FORWARD-MIB, ipCidrRouteTable (RFC 2096, deprecated). Under 1.3.6.1.2.1.4.24.4.1. ---

IP_CIDR_ROUTE_TABLE: Final = "1.3.6.1.2.1.4.24.4.1"
"""``ipCidrRouteEntry`` — IPv4 only. Its index is ``dest.mask.tos.nextHop``: thirteen
sub-identifiers, always, which makes it far easier to read than its successor."""

IP_CIDR_ROUTE_NEXT_HOP: Final = "1.3.6.1.2.1.4.24.4.1.4"
IP_CIDR_ROUTE_IF_INDEX: Final = "1.3.6.1.2.1.4.24.4.1.5"
IP_CIDR_ROUTE_TYPE: Final = "1.3.6.1.2.1.4.24.4.1.6"
"""``ipCidrRouteType`` — other(1), reject(2), local(3), remote(4)."""

IP_CIDR_ROUTE_TYPE_REMOTE: Final = 4

# --- RFC 1213, ipRouteTable. Columns under 1.3.6.1.2.1.4.21.1. ---

IP_ROUTE_TABLE: Final = "1.3.6.1.2.1.4.21.1"
"""``ipRouteEntry`` — the oldest routing table, indexed by destination alone. The last fallback."""

IP_ROUTE_IF_INDEX: Final = "1.3.6.1.2.1.4.21.1.2"
IP_ROUTE_NEXT_HOP: Final = "1.3.6.1.2.1.4.21.1.7"
IP_ROUTE_TYPE: Final = "1.3.6.1.2.1.4.21.1.8"
"""``ipRouteType`` — other(1), invalid(2), direct(3), indirect(4)."""

IP_ROUTE_TYPE_INDIRECT: Final = 4

# --- Q-BRIDGE-MIB, the VLAN tables (RFC 4363). Under 1.3.6.1.2.1.17.7.1.4. ---

DOT1Q_VLAN_STATIC_TABLE: Final = "1.3.6.1.2.1.17.7.1.4.3.1"
"""``dot1qVlanStaticEntry`` — the VLANs an operator configured, indexed by the VLAN id itself.

The only standard table that carries a VLAN's *name*, which is why it is read first. Its
membership columns are ``PortList`` bitmaps over bridge ports rather than over interfaces, so
they need the same ``dot1dBasePortTable`` hop the forwarding database needs.
"""

DOT1Q_VLAN_STATIC_NAME: Final = "1.3.6.1.2.1.17.7.1.4.3.1.1"
"""``dot1qVlanStaticName`` — what the operator called it."""

DOT1Q_VLAN_STATIC_EGRESS_PORTS: Final = "1.3.6.1.2.1.17.7.1.4.3.1.2"
"""``dot1qVlanStaticEgressPorts`` — every port the VLAN is configured on, tagged or not."""

DOT1Q_VLAN_STATIC_UNTAGGED_PORTS: Final = "1.3.6.1.2.1.17.7.1.4.3.1.4"
"""``dot1qVlanStaticUntaggedPorts`` — the subset that leaves untagged. An access port, usually."""

DOT1Q_VLAN_STATIC_ROW_STATUS: Final = "1.3.6.1.2.1.17.7.1.4.3.1.5"
"""``dot1qVlanStaticRowStatus`` — active(1), notInService(2), notReady(3), and the write values."""

DOT1Q_VLAN_STATIC_ROW_STATUS_ACTIVE: Final = 1

DOT1Q_VLAN_CURRENT_TABLE: Final = "1.3.6.1.2.1.17.7.1.4.2.1"
"""``dot1qVlanCurrentEntry`` — the VLANs the bridge is actually running, names excluded.

The fallback, reached only when the static table produced nothing, for the reason
``ipNetToMediaTable`` and ``dot1dTpFdbTable`` are fallbacks: a device implementing both would
otherwise be walked twice. Its index is ``dot1qVlanTimeMark.dot1qVlanIndex``, so the VLAN id is
the *second* sub-identifier and not the first.
"""

DOT1Q_VLAN_CURRENT_EGRESS_PORTS: Final = "1.3.6.1.2.1.17.7.1.4.2.1.4"
DOT1Q_VLAN_CURRENT_UNTAGGED_PORTS: Final = "1.3.6.1.2.1.17.7.1.4.2.1.5"

DOT1Q_VLAN_STATUS: Final = "1.3.6.1.2.1.17.7.1.4.2.1.6"
"""``dot1qVlanStatus`` — other(1), permanent(2), dynamicGvrp(3)."""

VLAN_ID_MIN: Final = 1
VLAN_ID_MAX: Final = 4094
"""``VlanIndex`` is ``1..4094``. A value outside it is not a VLAN and is dropped."""
