"""The ``Discover`` executor, and the range sweep behind one of its two walks.

``ExecutorRegistry`` is keyed by job kind and refuses a second executor for a kind, which is
correct — two things claiming to answer for ``Discover`` is a mistake, not a merge. But a
``Discover`` is now two different pieces of work: WP-1.5's SNMP fingerprint walk of one device,
and WP-1.6's sweep of a range that holds no devices yet.

So the discrimination happens a level down, on the same ``walk`` member the API's own result
handlers filter on. :class:`DiscoverExecutor` is the one executor for the kind and dispatches to
the walk that recognises the job; a walk this build does not have is reported as a failure naming
the reason, exactly as an unregistered kind is. Nothing about the collector's wire contract
changes for it: no ``JobKind`` member, no field on the lease, the result or the heartbeat.
"""

from __future__ import annotations

from typing import Any, Final, Protocol, runtime_checkable

import structlog
from pydantic import Field, ValidationError

from collector.discovery.sweep import SweepOutcome, sweep
from collector.models import JobKind, LeasedJob, WireModel

_LOG: Final = structlog.get_logger(__name__)

SWEEP_NAME: Final = "sweep"
"""The discriminator the API writes into a range sweep's parameters."""


class DiscoveryJobError(RuntimeError):
    """This job cannot be run as the walk it names, and nothing was probed."""


@runtime_checkable
class DiscoveryWalk(Protocol):
    """One kind of ``Discover`` work.

    The same shape as ``JobExecutor`` one level down, and deliberately structural: the SNMP walk
    lives in ``collector.snmp`` and does not import anything from here, so the two packages stay
    independent and ``__main__`` is the only place that knows both exist.
    """

    walk: str
    """Which walk this answers for, matching the job's ``parameters.walk``."""

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Do the work and return what was found, as the shape that walk's package defines."""
        ...


class DiscoverExecutor:
    """Runs whichever ``Discover`` walk a job names."""

    kind = JobKind.DISCOVER

    def __init__(self, walks: list[DiscoveryWalk]) -> None:
        self._walks: dict[str, DiscoveryWalk] = {}

        for walk in walks:
            self.register(walk)

    def register(self, walk: DiscoveryWalk) -> None:
        """Adds one walk. Registering a walk twice is a mistake, not a merge."""
        if walk.walk in self._walks:
            raise ValueError(f"A walk named {walk.walk} is already registered.")

        self._walks[walk.walk] = walk

    def __len__(self) -> int:
        return len(self._walks)

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Hand the job to the walk it names.

        Raising is how a job is failed. A job with no parameters, with parameters naming no walk,
        or naming a walk this build cannot run has had nothing done to it — which is what lets
        the API leave every fingerprint and every candidate exactly as it was.
        """
        name = self._walk_name(job)
        walk = self._walks.get(name)

        if walk is None:
            raise DiscoveryJobError(
                f"This collector has no {name} walk. It runs: {', '.join(sorted(self._walks))}."
            )

        return await walk.execute(job)

    @staticmethod
    def _walk_name(job: LeasedJob) -> str:
        if job.parameters is None:
            raise DiscoveryJobError(
                "A Discover job carries no parameters saying which walk to run."
            )

        name = job.parameters.get("walk")

        if not isinstance(name, str) or not name:
            raise DiscoveryJobError("A Discover job's parameters do not name a walk.")

        return name


class RangeSweepJobParameters(WireModel):
    """What the API asked for. Mirrors ``RangeSweepParameters`` on the other side."""

    walk: str
    first_address: str
    last_address: str
    exclusions: list[str] = Field(default_factory=list)
    count: int = Field(ge=1, le=10)
    timeout_seconds: float = Field(gt=0, le=30)
    interval_seconds: float = Field(ge=0, le=10)
    concurrency: int = Field(ge=1, le=512)
    max_responders: int = Field(ge=1, le=4096)


class RangeSweepExecutor:
    """Sweeps one span of addresses looking for anything that answers."""

    walk = SWEEP_NAME

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Probe the job's span and return the addresses that answered.

        A sweep names no device and carries no credential, which is the whole point of it: it is
        looking for hosts that are not devices yet, and an echo request authenticates to nothing.
        A span where nothing answers is a *successful* job with an empty list — the API tells
        that apart from a sweep that could not be performed, and only the first is evidence.
        """
        parameters = self._parameters(job)

        outcome = await sweep(
            parameters.first_address,
            parameters.last_address,
            exclusions=parameters.exclusions,
            count=parameters.count,
            reply_timeout_seconds=parameters.timeout_seconds,
            interval_seconds=parameters.interval_seconds,
            concurrency=parameters.concurrency,
            max_responders=parameters.max_responders,
        )

        _LOG.info(
            "collector.discovery.swept",
            jobId=str(job.job_id),
            firstAddress=outcome.first_address,
            lastAddress=outcome.last_address,
            scanned=outcome.scanned,
            responded=len(outcome.responders),
        )

        return payload(outcome)

    @staticmethod
    def _parameters(job: LeasedJob) -> RangeSweepJobParameters:
        if job.parameters is None:
            raise DiscoveryJobError("A sweep job carries no parameters saying what to sweep.")

        try:
            parameters = RangeSweepJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise DiscoveryJobError(
                f"The job parameters are not a range sweep's: {error}"
            ) from error

        if parameters.walk != SWEEP_NAME:
            raise DiscoveryJobError(
                f"This walk runs the {SWEEP_NAME} and this job names {parameters.walk}."
            )

        return parameters


def payload(outcome: SweepOutcome) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.
    """
    return {
        "walk": SWEEP_NAME,
        "firstAddress": outcome.first_address,
        "lastAddress": outcome.last_address,
        "scanned": outcome.scanned,
        "excluded": outcome.excluded,
        "truncated": outcome.truncated,
        "responders": [
            {"address": responder.address, "rttMilliseconds": responder.rtt_milliseconds}
            for responder in outcome.responders
        ],
    }
