"""The VLAN inventory: which VLANs a bridge is configured with, and which of its ports each is on.

``dot1qVlanStaticTable`` is the configured set and the only standard table carrying a VLAN's
*name*, so it is read first; ``dot1qVlanCurrentTable`` is the running set with no names and is
reached only when the static table produced nothing — the fallback shape :mod:`collector.snmp.arp`
and :mod:`collector.snmp.bridge` already use, and for the same reason, which is that a device
implementing both should not be walked twice.

**Membership is a bitmap over bridge ports, not over interfaces.** A ``PortList`` is an octet
string in which the most significant bit of the first octet is bridge port 1, and a bridge port
is a number local to the bridge. The rest of NetShield speaks ``ifIndex``, so every bit has to go
through ``dot1dBasePortTable`` exactly as a forwarding entry does — WP-1.8 wrote that join and
this reuses it rather than making a second one that could disagree.

**It is a walk of its own**, beside the neighbour walk and the route walk, for the reason those
two are separate from each other: a VLAN configuration changes when somebody re-designs the
network rather than when somebody re-cables it, so it earns a much longer interval, and an agent
that cannot answer the Q-BRIDGE tables should not cost a device the LLDP edges another job
already established.

Read only, and standard only. No vendor-private VLAN MIB is consulted: a device answering neither
Q-BRIDGE table reports ``supported: false`` and the API withdraws nothing, which is a smaller harm
than guessing at an enterprise subtree the vendor seam exists to keep out of shared code.
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Any, Final

import structlog
from pydantic import Field, ValidationError

from collector.models import LeasedJob, WireModel
from collector.snmp import oids
from collector.snmp.bridge import parse_base_ports
from collector.snmp.octets import octets
from collector.snmp.session import PySnmpSession, SnmpSession, SnmpSessionFactory
from collector.snmp.tables import number, rows, text

_LOG: Final = structlog.get_logger(__name__)

STATIC_TABLE: Final = "dot1qVlanStatic"
CURRENT_TABLE: Final = "dot1qVlanCurrent"
"""What the reading records about where it came from, for the scan row to show an operator."""


@dataclass(frozen=True, slots=True)
class VlanRecord:
    """One VLAN as one device is configured with it."""

    vlan_id: int

    name: str | None
    """What this device calls it. Absent from the current table, which carries no names."""

    if_indexes: tuple[int, ...]
    """Every member port, as an ``ifIndex``, ascending."""

    untagged_if_indexes: tuple[int, ...]
    """The members that leave untagged — an access port, usually. A subset of the above."""

    port_count: int
    """How many bridge ports the bitmap named, before any were resolved."""

    unresolved_port_count: int
    """How many of them ``dot1dBasePortTable`` could not place.

    Reported rather than hidden. A switch whose base-port table is unreadable would otherwise
    look like one whose VLANs have no ports on them, and those are different facts.
    """


@dataclass(frozen=True, slots=True)
class VlanReading:
    """What one device said about its VLANs, and whether it said anything at all.

    ``supported`` carries the weight it does everywhere else in this package. A device that
    implements neither Q-BRIDGE table — a router, an access point, anything that is not a bridge —
    was never asked a question it could answer, and reporting that as "this device has no VLANs"
    would let an absence withdraw an inventory nobody contradicted.
    """

    supported: bool
    vlans: tuple[VlanRecord, ...] = ()
    truncated: bool = False
    table: str | None = None


async def read_vlans(
    session: SnmpSession,
    *,
    max_rows: int,
    max_records: int,
) -> VlanReading:
    """Read the device's VLAN inventory, preferring the table that carries names."""
    base_ports = parse_base_ports(await session.walk(oids.DOT1D_BASE_PORT_TABLE, max_rows=max_rows))

    static = parse_static(
        await session.walk(oids.DOT1Q_VLAN_STATIC_TABLE, max_rows=max_rows),
        base_ports,
    )

    if static:
        _LOG.debug("collector.snmp.vlans-read", table=STATIC_TABLE, vlans=len(static))

        return _bounded(static, max_records, table=STATIC_TABLE)

    current = parse_current(
        await session.walk(oids.DOT1Q_VLAN_CURRENT_TABLE, max_rows=max_rows),
        base_ports,
    )

    if current:
        _LOG.debug("collector.snmp.vlans-read", table=CURRENT_TABLE, vlans=len(current))

        return _bounded(current, max_records, table=CURRENT_TABLE)

    return VlanReading(supported=False)


def parse_static(
    walked: Mapping[str, str],
    base_ports: Mapping[int, int],
) -> tuple[VlanRecord, ...]:
    """Rows of ``dot1qVlanStaticTable``, as records. Indexed by the VLAN id alone.

    A row whose status says it is not active is dropped: ``notReady`` and ``notInService`` are a
    VLAN being built or taken out, and recording one would put a VLAN in the inventory the bridge
    is not running. A row with no status column at all is kept, because plenty of agents omit it
    and treating silence as inactive would empty the table on those.
    """
    records: list[VlanRecord] = []

    for index, row in rows(walked, oids.DOT1Q_VLAN_STATIC_TABLE).items():
        vlan_id = _vlan_id(index)

        if vlan_id is None:
            continue

        status = number(row, oids.DOT1Q_VLAN_STATIC_ROW_STATUS)

        if status is not None and status != oids.DOT1Q_VLAN_STATIC_ROW_STATUS_ACTIVE:
            continue

        records.append(
            _record(
                vlan_id,
                text(row, oids.DOT1Q_VLAN_STATIC_NAME),
                row.get(oids.DOT1Q_VLAN_STATIC_EGRESS_PORTS),
                row.get(oids.DOT1Q_VLAN_STATIC_UNTAGGED_PORTS),
                base_ports,
            )
        )

    return tuple(sorted(records, key=lambda record: record.vlan_id))


def parse_current(
    walked: Mapping[str, str],
    base_ports: Mapping[int, int],
) -> tuple[VlanRecord, ...]:
    """Rows of ``dot1qVlanCurrentTable``, as records.

    Indexed by ``dot1qVlanTimeMark.dot1qVlanIndex``, so the VLAN id is the second sub-identifier.
    The time mark is a row-age marker for a manager doing an incremental read; NetShield reads the
    whole table every time, so the newest entry for a VLAN wins and the rest are discarded.
    """
    latest: dict[int, tuple[int, VlanRecord]] = {}

    for index, row in rows(walked, oids.DOT1Q_VLAN_CURRENT_TABLE).items():
        parsed = _current_index(index)

        if parsed is None:
            continue

        time_mark, vlan_id = parsed

        record = _record(
            vlan_id,
            None,
            row.get(oids.DOT1Q_VLAN_CURRENT_EGRESS_PORTS),
            row.get(oids.DOT1Q_VLAN_CURRENT_UNTAGGED_PORTS),
            base_ports,
        )

        seen = latest.get(vlan_id)

        if seen is None or time_mark >= seen[0]:
            latest[vlan_id] = (time_mark, record)

    return tuple(
        record for _, record in sorted(latest.values(), key=lambda entry: entry[1].vlan_id)
    )


def port_list(value: str | None) -> tuple[int, ...]:
    """The bridge ports a ``PortList`` bitmap names, ascending.

    RFC 4363: each octet carries eight ports, the first octet carries ports 1 to 8, and within an
    octet the *most* significant bit is the lowest numbered port.
    """
    raw = octets(value)
    ports: list[int] = []

    for offset, byte in enumerate(raw):
        for bit in range(8):
            if byte & (0x80 >> bit):
                ports.append(offset * 8 + bit + 1)

    return tuple(ports)


def _record(
    vlan_id: int,
    name: str | None,
    egress: str | None,
    untagged: str | None,
    base_ports: Mapping[int, int],
) -> VlanRecord:
    """One VLAN's row, with both bitmaps resolved from bridge ports to interface indexes."""
    members = port_list(egress)
    untagged_members = port_list(untagged)

    resolved = _resolve(members, base_ports)

    # The untagged set is a subset of the egress set by definition, and an agent that disagrees
    # with its own definition is not grounds for dropping a port: the union is what membership
    # means, so an untagged port missing from the egress bitmap is added rather than argued with.
    resolved_untagged = _resolve(untagged_members, base_ports)

    return VlanRecord(
        vlan_id=vlan_id,
        name=name,
        if_indexes=tuple(sorted(set(resolved) | set(resolved_untagged))),
        untagged_if_indexes=tuple(sorted(set(resolved_untagged))),
        port_count=len(set(members) | set(untagged_members)),
        unresolved_port_count=sum(
            1 for port in set(members) | set(untagged_members) if port not in base_ports
        ),
    )


def _resolve(ports: Sequence[int], base_ports: Mapping[int, int]) -> tuple[int, ...]:
    """Bridge ports as interface indexes, dropping the ones the base-port table cannot place.

    Dropped rather than reported as a bare number, exactly as a forwarding entry naming an unknown
    bridge port is: a port number means nothing outside the bridge that issued it, and a VLAN
    member nothing else in the inventory can identify is not a member anything can be said about.
    """
    return tuple(base_ports[port] for port in ports if port in base_ports)


def _vlan_id(index: str) -> int | None:
    """One sub-identifier as a VLAN id, or nothing when it is not one."""
    if not index.isdigit():
        return None

    vlan_id = int(index)

    return vlan_id if oids.VLAN_ID_MIN <= vlan_id <= oids.VLAN_ID_MAX else None


def _current_index(index: str) -> tuple[int, int] | None:
    """``dot1qVlanTimeMark.dot1qVlanIndex`` as the two numbers it is."""
    parts = index.split(".")

    if len(parts) != 2 or not all(part.isdigit() for part in parts):
        return None

    vlan_id = _vlan_id(parts[1])

    return None if vlan_id is None else (int(parts[0]), vlan_id)


def _bounded(records: tuple[VlanRecord, ...], max_records: int, *, table: str) -> VlanReading:
    """The reading, cut to what one result may carry, saying so when it was cut."""
    if len(records) <= max_records:
        return VlanReading(supported=True, vlans=records, table=table)

    _LOG.warning("collector.snmp.vlans-truncated", found=len(records), reported=max_records)

    return VlanReading(
        supported=True,
        vlans=records[:max_records],
        truncated=True,
        table=table,
    )


WALK_NAME: Final = "vlans"
"""The discriminator the API writes into a VLAN walk's parameters."""


class VlanWalkJobError(RuntimeError):
    """This job cannot be run as a VLAN walk, and no table was read."""


class VlanWalkJobParameters(WireModel):
    """What the API asked for. Mirrors ``VlanWalkParameters`` on the other side."""

    walk: str
    timeout_seconds: float = Field(gt=0, le=120)
    retries: int = Field(ge=0, le=10)
    max_repetitions: int = Field(ge=1, le=100)
    max_rows: int = Field(ge=1, le=200_000)
    max_vlans: int = Field(ge=1, le=4_094)


class VlanWalkExecutor:
    """Reads one device's VLAN inventory."""

    walk = WALK_NAME
    """Which ``Discover`` walk this answers for. ``DiscoverExecutor`` dispatches on it."""

    def __init__(self, session_factory: SnmpSessionFactory | None = None) -> None:
        self._session = session_factory or PySnmpSession

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Read the job's device and return the VLANs it is configured with.

        Raising is how a job is failed, and it means nothing was established — the API leaves the
        device's VLAN rows exactly as they were. That matters here for the reason it matters for
        the neighbour walk: this walk *withdraws* a VLAN a successful reading no longer contains,
        so a failure applied as an empty reading would empty the inventory of every switch that
        stopped answering SNMP.
        """
        if job.device is None:
            raise VlanWalkJobError("A VLAN walk needs a device and this job names none.")

        if job.credential is None:
            raise VlanWalkJobError("A VLAN walk needs a credential and this job carries none.")

        parameters = self._parameters(job)

        async with self._session(
            job.device.ip_address,
            job.credential,
            timeout_seconds=parameters.timeout_seconds,
            retries=parameters.retries,
            max_repetitions=parameters.max_repetitions,
        ) as session:
            reading = await read_vlans(
                session,
                max_rows=parameters.max_rows,
                max_records=parameters.max_vlans,
            )

        _LOG.info(
            "collector.snmp.vlans-walked",
            jobId=str(job.job_id),
            deviceId=str(job.device.device_id),
            supported=reading.supported,
            table=reading.table,
            vlans=len(reading.vlans),
        )

        return payload(reading)

    @staticmethod
    def _parameters(job: LeasedJob) -> VlanWalkJobParameters:
        if job.parameters is None:
            raise VlanWalkJobError("A Discover job carries no parameters saying which walk to run.")

        try:
            parameters = VlanWalkJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise VlanWalkJobError(f"The job parameters are not a VLAN walk's: {error}") from error

        if parameters.walk != WALK_NAME:
            raise VlanWalkJobError(
                f"This walk runs the {WALK_NAME} walk and this job names {parameters.walk}."
            )

        return parameters


def payload(reading: VlanReading) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.

    No port *names* travel: unlike a neighbour's far end, these are the observing device's own
    ports, and the API already holds a name for every one of them in ``device_interfaces``.
    """
    return {
        "walk": WALK_NAME,
        "vlansSupported": reading.supported,
        "vlanTable": reading.table,
        "vlanCount": len(reading.vlans),
        "vlansTruncated": reading.truncated,
        "vlans": [
            {
                "vlanId": record.vlan_id,
                "name": record.name,
                "ifIndexes": list(record.if_indexes),
                "untaggedIfIndexes": list(record.untagged_if_indexes),
                "portCount": record.port_count,
                "unresolvedPortCount": record.unresolved_port_count,
            }
            for record in reading.vlans
        ],
    }
