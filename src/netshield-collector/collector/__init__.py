"""NetShield's pull collector.

The collector is a dumb producer (ARCHITECTURE.md §2). It asks the API for work, does it, and
reports what it found. It decides nothing: not what to poll, not how often, not which credential
to use, not what a result means. Every one of those is the API's, and the collector is told.

Three rules hold everywhere in this package:

* It never writes to a network device. There is no SNMP ``set``, no configuration mode, and no
  command outside a per-vendor read-only allowlist (SPEC.md §3, ARCHITECTURE.md §1).
* It never persists a device credential. Credentials arrive per job, live in memory for as long
  as the job runs, and reach neither disk nor a log line (ARCHITECTURE.md §7).
* It holds no database credential and never opens a database connection.
"""

__all__ = ["__version__"]

__version__ = "0.1.0"
