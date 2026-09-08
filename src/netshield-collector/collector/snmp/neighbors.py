"""The fourth ``Discover`` walk: reading one device's neighbour protocols.

Beside WP-1.5's ``snmp`` fingerprint, WP-1.6's ``sweep`` and WP-1.8's ``clients``, dispatched by
``DiscoverExecutor`` on the same ``walk`` member the API's own result handlers filter on. Nothing
about the collector's wire contract changes for it: no ``JobKind`` member, no field on the lease,
the result or the heartbeat.

**It asks both protocols, and which ones it asks is the vendor's decision.** LLDP is IEEE 802.1AB
and every platform SPEC.md §4 names implements it; CDP is Cisco's and lives in Cisco's private
MIB, so only the two Cisco adapters declare it. That declaration is what keeps the ``if`` chain
CONVENTIONS.md §5 forbids from ever starting — shared code asks the adapter and never branches on
a vendor name. A device whose vendor this build does not recognise is asked for LLDP alone,
because the generic adapter declares the standard protocol and guessing at a private MIB is
exactly the guess the vendor seam exists to prevent.

The routing table is deliberately *not* here. See :mod:`collector.snmp.routes`.
"""

from __future__ import annotations

from typing import Any, Final

import structlog
from pydantic import Field, ValidationError

from collector.models import LeasedJob, WireModel
from collector.snmp.cdp import CdpReading, read_cdp
from collector.snmp.lldp import LldpReading, read_lldp
from collector.snmp.ports import read_port_names
from collector.snmp.session import PySnmpSession, SnmpSessionFactory
from collector.vendors.base import VendorRegistry

_LOG: Final = structlog.get_logger(__name__)

WALK_NAME: Final = "neighbors"
"""The discriminator the API writes into a neighbour walk's parameters."""

LLDP: Final = "Lldp"
CDP: Final = "Cdp"
"""The neighbour protocols, spelled as the API's ``NeighborSource`` spells them."""


class NeighborWalkJobError(RuntimeError):
    """This job cannot be run as a neighbour walk, and no protocol was read."""


class NeighborWalkJobParameters(WireModel):
    """What the API asked for. Mirrors ``NeighborWalkParameters`` on the other side."""

    walk: str
    timeout_seconds: float = Field(gt=0, le=120)
    retries: int = Field(ge=0, le=10)
    max_repetitions: int = Field(ge=1, le=100)
    max_rows: int = Field(ge=1, le=200_000)
    max_neighbors: int = Field(ge=1, le=50_000)


class NeighborWalkExecutor:
    """Reads one device's LLDP and CDP tables."""

    walk = WALK_NAME
    """Which ``Discover`` walk this answers for. ``DiscoverExecutor`` dispatches on it."""

    def __init__(
        self,
        vendors: VendorRegistry,
        session_factory: SnmpSessionFactory | None = None,
    ) -> None:
        self._vendors = vendors
        self._session = session_factory or PySnmpSession

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Read the job's device and return what it can see.

        Raising is how a job is failed, and it means nothing was established — the API leaves
        every adjacency exactly as it was. That matters more here than for a fingerprint, for the
        reason it matters for a client walk: a failed walk applied as an empty one would withdraw
        every edge on the device because the device stopped answering SNMP, and Phase 6's
        topology-aware suppression would then be reasoning about a network that had fallen apart
        on paper.
        """
        if job.device is None:
            raise NeighborWalkJobError("A neighbour walk needs a device and this job names none.")

        if job.credential is None:
            raise NeighborWalkJobError(
                "A neighbour walk needs a credential and this job carries none."
            )

        parameters = self._parameters(job)
        protocols = self._protocols(job.device.vendor)

        async with self._session(
            job.device.ip_address,
            job.credential,
            timeout_seconds=parameters.timeout_seconds,
            retries=parameters.retries,
            max_repetitions=parameters.max_repetitions,
        ) as session:
            ports = await read_port_names(session, max_rows=parameters.max_rows)

            lldp = (
                await read_lldp(
                    session,
                    ports,
                    max_rows=parameters.max_rows,
                    max_records=parameters.max_neighbors,
                )
                if LLDP in protocols
                else LldpReading(supported=False)
            )

            cdp = (
                await read_cdp(
                    session,
                    ports,
                    max_rows=parameters.max_rows,
                    max_records=parameters.max_neighbors,
                )
                if CDP in protocols
                else CdpReading(supported=False)
            )

        _LOG.info(
            "collector.snmp.neighbors-read",
            jobId=str(job.job_id),
            deviceId=str(job.device.device_id),
            protocols=sorted(protocols),
            lldpSupported=lldp.supported,
            lldp=len(lldp.neighbors),
            cdpSupported=cdp.supported,
            cdp=len(cdp.neighbors),
        )

        return payload(lldp, cdp)

    def _protocols(self, vendor: str | None) -> frozenset[str]:
        """Which neighbour protocols to ask this device for.

        The adapter decides. A vendor with no adapter registered — which is the state a build
        with an empty registry is in — falls back to LLDP, because that is the protocol the
        standard defines and a device that speaks none of them simply answers nothing.
        """
        adapter = self._vendors.for_vendor(vendor or "") if vendor else None

        if adapter is None:
            return frozenset({LLDP})

        return frozenset(adapter.neighbor_protocols)

    @staticmethod
    def _parameters(job: LeasedJob) -> NeighborWalkJobParameters:
        if job.parameters is None:
            raise NeighborWalkJobError(
                "A Discover job carries no parameters saying which walk to run."
            )

        try:
            parameters = NeighborWalkJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise NeighborWalkJobError(
                f"The job parameters are not a neighbour walk's: {error}"
            ) from error

        if parameters.walk != WALK_NAME:
            raise NeighborWalkJobError(
                f"This walk runs the {WALK_NAME} walk and this job names {parameters.walk}."
            )

        return parameters


def payload(lldp: LldpReading, cdp: CdpReading) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.
    """
    return {
        "walk": WALK_NAME,
        "localChassisId": lldp.local_chassis_id,
        "localChassisIdKind": lldp.local_chassis_id_kind,
        "localSystemName": lldp.local_system_name,
        "lldpSupported": lldp.supported,
        "lldpCount": len(lldp.neighbors),
        "lldpTruncated": lldp.truncated,
        "lldp": [
            {
                "localIfIndex": neighbor.local_if_index,
                "localPortName": neighbor.local_port_name,
                "chassisId": neighbor.chassis_id,
                "chassisIdKind": neighbor.chassis_id_kind,
                "portId": neighbor.port_id,
                "portIdKind": neighbor.port_id_kind,
                "portDescription": neighbor.port_description,
                "systemName": neighbor.system_name,
                "systemDescription": neighbor.system_description,
                "managementAddress": neighbor.management_address,
                "capabilities": neighbor.capabilities,
            }
            for neighbor in lldp.neighbors
        ],
        "cdpSupported": cdp.supported,
        "cdpCount": len(cdp.neighbors),
        "cdpTruncated": cdp.truncated,
        "cdp": [
            {
                "localIfIndex": neighbor.local_if_index,
                "localPortName": neighbor.local_port_name,
                "deviceId": neighbor.device_id,
                "devicePort": neighbor.device_port,
                "platform": neighbor.platform,
                "version": neighbor.version,
                "address": neighbor.address,
                "capabilities": neighbor.capabilities,
            }
            for neighbor in cdp.neighbors
        ],
    }
