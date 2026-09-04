"""The job executor that turns a leased ``Discover`` job into an SNMP walk.

The second ``JobExecutor`` NetShield has, and the first to need a credential: WP-1.2 sealed them,
WP-1.3 delivered them over the lease, WP-1.4's ICMP probe needed none, and this is where the
whole path is exercised. The credential is read out of the leased job, used to build one session,
and never written anywhere (ARCHITECTURE.md §7).

It answers for ``Discover`` jobs whose parameters name the SNMP walk. A ``Discover`` naming
anything else is reported as a failure with the reason, exactly as the ICMP executor refuses a
``Poll`` for another probe — WP-1.6's range sweep will be a ``Discover`` too, and each side has
to recognise its own rather than answer the other's wrongly.
"""

from __future__ import annotations

from typing import Any, Final

import structlog
from pydantic import Field, ValidationError

from collector.models import JobKind, LeasedJob, WireModel
from collector.snmp.fingerprint import WalkOutcome, walk_device
from collector.snmp.interfaces import InterfaceRecord
from collector.snmp.session import PySnmpSession, SnmpSessionFactory
from collector.vendors.base import VendorRegistry

_LOG: Final = structlog.get_logger(__name__)

WALK_NAME: Final = "snmp"
"""The discriminator the API writes into a fingerprint job's parameters."""


class SnmpJobError(RuntimeError):
    """This job cannot be run as an SNMP walk, and no walk was attempted."""


class SnmpWalkJobParameters(WireModel):
    """What the API asked for. Mirrors ``SnmpWalkParameters`` on the other side."""

    walk: str
    timeout_seconds: float = Field(gt=0, le=120)
    retries: int = Field(ge=0, le=10)
    max_repetitions: int = Field(ge=1, le=100)
    max_rows: int = Field(ge=1, le=100_000)
    max_interfaces: int = Field(ge=1, le=10_000)


class SnmpWalkExecutor:
    """Runs one fingerprint walk."""

    kind = JobKind.DISCOVER

    def __init__(
        self,
        vendors: VendorRegistry,
        session_factory: SnmpSessionFactory | None = None,
    ) -> None:
        self._vendors = vendors
        self._session = session_factory or PySnmpSession

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Walk the job's device and return what it turned out to be.

        Raising is how a job is failed. Everything raised from here — no device, no credential, a
        credential of the wrong kind, parameters that are not a walk's, or a device that did not
        answer — means the walk did not establish anything, and the API leaves the device's
        recorded fingerprint exactly as it was.
        """
        if job.device is None:
            raise SnmpJobError("An SNMP walk needs a device and this job names none.")

        if job.credential is None:
            raise SnmpJobError("An SNMP walk needs a credential and this job carries none.")

        parameters = self._parameters(job)

        async with self._session(
            job.device.ip_address,
            job.credential,
            timeout_seconds=parameters.timeout_seconds,
            retries=parameters.retries,
            max_repetitions=parameters.max_repetitions,
        ) as session:
            outcome = await walk_device(
                session,
                self._vendors,
                max_rows=parameters.max_rows,
                max_interfaces=parameters.max_interfaces,
            )

        _LOG.info(
            "collector.snmp.walked",
            jobId=str(job.job_id),
            deviceId=str(job.device.device_id),
            vendor=outcome.vendor,
            interfaces=outcome.interface_count,
        )

        return payload(outcome)

    @staticmethod
    def _parameters(job: LeasedJob) -> SnmpWalkJobParameters:
        if job.parameters is None:
            raise SnmpJobError("A Discover job carries no parameters saying which walk to run.")

        try:
            parameters = SnmpWalkJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise SnmpJobError(f"The job parameters are not an SNMP walk's: {error}") from error

        if parameters.walk != WALK_NAME:
            raise SnmpJobError(
                f"This collector runs the {WALK_NAME} walk and this job names {parameters.walk}."
            )

        return parameters


def payload(outcome: WalkOutcome) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.
    """
    system = outcome.system
    facts = outcome.facts

    return {
        "walk": WALK_NAME,
        "vendor": outcome.vendor,
        "reducedCapability": outcome.reduced_capability,
        "sysObjectId": system.object_id,
        "sysDescr": system.descr,
        "sysName": system.name,
        "sysContact": system.contact,
        "sysLocation": system.location,
        "uptimeSeconds": system.uptime_seconds,
        "model": facts.model,
        "osVersion": facts.os_version,
        "serialNumber": facts.serial_number,
        "interfaceCount": outcome.interface_count,
        "interfacesTruncated": outcome.interfaces_truncated,
        "interfaces": [_interface(record) for record in outcome.interfaces],
    }


def _interface(record: InterfaceRecord) -> dict[str, Any]:
    return {
        "index": record.index,
        "name": record.name,
        "description": record.description,
        "alias": record.alias,
        "interfaceType": record.interface_type,
        "mtu": record.mtu,
        "speedBitsPerSecond": record.speed_bits_per_second,
        "physicalAddress": record.physical_address,
        "adminStatus": record.admin_status,
        "operStatus": record.oper_status,
    }
