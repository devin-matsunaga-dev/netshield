"""The job executor that turns a leased ``Poll`` job into an ICMP probe.

This is the first ``JobExecutor`` NetShield has. Until it existed, every build leased a job,
found no executor for its kind and reported the job as a failure naming that reason — which is
the state WP-1.3 deliberately shipped in.

It answers for ``Poll`` jobs whose parameters name the ICMP probe. A ``Poll`` job naming a probe
this build does not implement is reported as a failure with the reason, exactly as an
unregistered kind is: the API's own result handler reads only rows whose parameters say ``icmp``,
so a job for someone else's probe is refused here rather than being answered wrongly.
"""

from __future__ import annotations

from typing import Any, Final

import structlog
from pydantic import Field, ValidationError

from collector.icmp.probe import ProbeOutcome, probe
from collector.models import JobKind, LeasedJob, WireModel

_LOG: Final = structlog.get_logger(__name__)

PROBE_NAME: Final = "icmp"
"""The discriminator the API writes into a reachability job's parameters."""


class IcmpJobError(RuntimeError):
    """This job cannot be run as an ICMP probe, and no probe was attempted."""


class IcmpJobParameters(WireModel):
    """What the API asked for. Mirrors ``IcmpProbeParameters`` on the other side."""

    probe: str
    count: int = Field(ge=1, le=20)
    timeout_seconds: float = Field(gt=0, le=30)
    interval_seconds: float = Field(ge=0, le=10)


class IcmpExecutor:
    """Runs one reachability probe."""

    kind = JobKind.POLL

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Probe the job's device and return what was observed.

        Raising is how a job is failed, and everything raised from here means the probe did not
        run: a job with no device, parameters that are not an ICMP probe's, an address that is
        not an address, or no ICMP socket at all. A probe that ran and heard nothing back is a
        successful job reporting a hundred per cent loss — the API tells those two apart, and
        only the second is evidence about the device.
        """
        if job.device is None:
            raise IcmpJobError("A reachability probe needs a device and this job names none.")

        parameters = self._parameters(job)

        outcome = await probe(
            job.device.ip_address,
            count=parameters.count,
            reply_timeout_seconds=parameters.timeout_seconds,
            interval_seconds=parameters.interval_seconds,
        )

        _LOG.info(
            "collector.icmp.probed",
            jobId=str(job.job_id),
            deviceId=str(job.device.device_id),
            sent=outcome.sent,
            received=outcome.received,
            lossPercent=outcome.loss_percent,
        )

        return _payload(outcome)

    @staticmethod
    def _parameters(job: LeasedJob) -> IcmpJobParameters:
        if job.parameters is None:
            raise IcmpJobError("A Poll job carries no parameters saying which probe to run.")

        try:
            parameters = IcmpJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise IcmpJobError(f"The job parameters are not an ICMP probe's: {error}") from error

        if parameters.probe != PROBE_NAME:
            raise IcmpJobError(
                f"This collector runs the {PROBE_NAME} probe and this job names {parameters.probe}."
            )

        return parameters


def _payload(outcome: ProbeOutcome) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.
    """
    round_trips = outcome.round_trips

    return {
        "probe": PROBE_NAME,
        "address": outcome.address,
        "sent": outcome.sent,
        "received": outcome.received,
        "lossPercent": outcome.loss_percent,
        "rttMillisecondsMin": min(round_trips) if round_trips else None,
        "rttMillisecondsMax": max(round_trips) if round_trips else None,
        "rttMillisecondsAvg": (
            round(sum(round_trips) / len(round_trips), 3) if round_trips else None
        ),
        "replies": [
            {"sequence": reply.sequence, "rttMilliseconds": reply.rtt_milliseconds}
            for reply in outcome.replies
        ],
    }
