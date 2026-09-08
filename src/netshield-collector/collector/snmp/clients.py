"""The third ``Discover`` walk: reading one device's client tables.

Beside WP-1.5's ``snmp`` fingerprint and WP-1.6's ``sweep``, dispatched by ``DiscoverExecutor``
on the same ``walk`` member the API's own result handlers filter on. Nothing about the collector's
wire contract changes for it: no ``JobKind`` member, no field on the lease, the result or the
heartbeat.

**It asks both questions of every device.** A router answers a neighbour cache and forwards
nothing; an access switch answers a forwarding database and has no ARP table worth reading; a
layer-3 switch answers both. Which of the two a device speaks is a property of the device, and
the alternative — having the API decide in advance from ``DeviceRole`` — would be deciding from
free text an operator typed. So the walk reads both, reports which it got, and lets the API tell
"this device does not implement that table" from "that table is empty".
"""

from __future__ import annotations

from typing import Any, Final

import structlog
from pydantic import Field, ValidationError

from collector.models import LeasedJob, WireModel
from collector.snmp.arp import NeighborReading, read_neighbors
from collector.snmp.bridge import ForwardingReading, read_forwarding
from collector.snmp.session import PySnmpSession, SnmpSessionFactory

_LOG: Final = structlog.get_logger(__name__)

WALK_NAME: Final = "clients"
"""The discriminator the API writes into a client walk's parameters."""


class ClientWalkJobError(RuntimeError):
    """This job cannot be run as a client walk, and no table was read."""


class ClientWalkJobParameters(WireModel):
    """What the API asked for. Mirrors ``ClientWalkParameters`` on the other side."""

    walk: str
    timeout_seconds: float = Field(gt=0, le=120)
    retries: int = Field(ge=0, le=10)
    max_repetitions: int = Field(ge=1, le=100)
    max_rows: int = Field(ge=1, le=200_000)
    max_neighbors: int = Field(ge=1, le=50_000)
    max_forwarding_entries: int = Field(ge=1, le=50_000)


class ClientWalkExecutor:
    """Reads one device's neighbour cache and forwarding database."""

    walk = WALK_NAME
    """Which ``Discover`` walk this answers for. ``DiscoverExecutor`` dispatches on it."""

    def __init__(self, session_factory: SnmpSessionFactory | None = None) -> None:
        self._session = session_factory or PySnmpSession

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Read the job's device and return who it can see.

        Raising is how a job is failed. Everything raised from here — no device, no credential, a
        credential of the wrong kind, parameters that are not this walk's, or a device that
        stopped answering partway through — means nothing was established, and the API leaves
        every binding exactly as it was. That matters more here than for a fingerprint: a failed
        walk applied as an empty one would report every endpoint behind a switch as having left
        because the switch stopped answering SNMP.
        """
        if job.device is None:
            raise ClientWalkJobError("A client walk needs a device and this job names none.")

        if job.credential is None:
            raise ClientWalkJobError("A client walk needs a credential and this job carries none.")

        parameters = self._parameters(job)

        async with self._session(
            job.device.ip_address,
            job.credential,
            timeout_seconds=parameters.timeout_seconds,
            retries=parameters.retries,
            max_repetitions=parameters.max_repetitions,
        ) as session:
            neighbors = await read_neighbors(
                session,
                max_rows=parameters.max_rows,
                max_records=parameters.max_neighbors,
            )
            forwarding = await read_forwarding(
                session,
                max_rows=parameters.max_rows,
                max_records=parameters.max_forwarding_entries,
            )

        _LOG.info(
            "collector.snmp.clients-read",
            jobId=str(job.job_id),
            deviceId=str(job.device.device_id),
            neighborsSupported=neighbors.supported,
            neighbors=len(neighbors.records),
            forwardingSupported=forwarding.supported,
            forwarding=len(forwarding.records),
        )

        return payload(neighbors, forwarding)

    @staticmethod
    def _parameters(job: LeasedJob) -> ClientWalkJobParameters:
        if job.parameters is None:
            raise ClientWalkJobError(
                "A Discover job carries no parameters saying which walk to run."
            )

        try:
            parameters = ClientWalkJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise ClientWalkJobError(
                f"The job parameters are not a client walk's: {error}"
            ) from error

        if parameters.walk != WALK_NAME:
            raise ClientWalkJobError(
                f"This walk runs the {WALK_NAME} walk and this job names {parameters.walk}."
            )

        return parameters


def payload(neighbors: NeighborReading, forwarding: ForwardingReading) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.
    """
    return {
        "walk": WALK_NAME,
        "neighborsSupported": neighbors.supported,
        "neighborCount": len(neighbors.records),
        "neighborsTruncated": neighbors.truncated,
        "neighbors": [
            {
                "ipAddress": record.ip_address,
                "macAddress": record.mac_address,
                "ifIndex": record.if_index,
            }
            for record in neighbors.records
        ],
        "forwardingSupported": forwarding.supported,
        "forwardingCount": len(forwarding.records),
        "forwardingTruncated": forwarding.truncated,
        "forwarding": [
            {
                "macAddress": record.mac_address,
                "ifIndex": record.if_index,
                "vlanId": record.vlan_id,
                "macCountOnPort": record.mac_count_on_port,
            }
            for record in forwarding.records
        ],
    }
