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
