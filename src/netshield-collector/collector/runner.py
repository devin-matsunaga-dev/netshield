"""The loop: ask for work, do it, report it, say hello, repeat."""

from __future__ import annotations

import asyncio
from typing import Any, Final

import structlog

from collector.api import CollectorApi, CollectorApiError
from collector.config import CollectorSettings
from collector.jobs import NO_EXECUTOR, ExecutorRegistry
from collector.logging import redact_text
from collector.models import JobOutcome, LeasedJob, ResultReport

_LOG: Final = structlog.get_logger(__name__)


class CollectorRunner:
    """Runs one collector: a work loop and a heartbeat, until it is asked to stop.

    Two things it deliberately does not do. It does not decide what to collect — it runs what it
    is handed. And it does not persist anything: there is no queue on disk, no result spool and
    no cache. A job whose result never reached the API is a job whose lease expires and which the
    API gives to somebody else, which is the recovery ARCHITECTURE.md §7 already describes and is
    strictly safer than a collector holding device data it was told to forget.
    """

    def __init__(
        self,
        api: CollectorApi,
        executors: ExecutorRegistry,
        settings: CollectorSettings,
    ) -> None:
        self._api = api
        self._executors = executors
        self._settings = settings
        self._stopping = asyncio.Event()
        self._slots = asyncio.Semaphore(settings.capacity)
        self._running = 0

        # The API's pacing, once it has answered a heartbeat. Until then the configured values
        # stand in, so a collector that starts before the API is reachable still behaves.
        self._poll_seconds = settings.poll_seconds
        self._max_jobs_per_lease = settings.capacity

    @property
    def running(self) -> int:
        """How many jobs are in flight."""
        return self._running

    def stop(self) -> None:
        """Ask the loops to finish the pass they are on and return."""
        self._stopping.set()

    async def run(self) -> None:
        """Run the work loop and the heartbeat until :meth:`stop` is called.

        Stopping cancels whatever is in flight rather than waiting for it. A request that is
        already out can take as long as the request timeout to come back, and an orchestrator
        that asked a container to go away will not wait that long before killing it — so the
        polite shutdown has to be the fast one. Nothing is lost by it: a job whose result never
        reached the API has its lease expire and is given to somebody else, which is the recovery
        the contract already describes.
        """
        _LOG.info(
            "collector.starting",
            collector=self._settings.collector_name,
            capacity=self._settings.capacity,
            executors=len(self._executors),
        )

        loops = [
            asyncio.create_task(self._heartbeat_loop(), name="heartbeat"),
            asyncio.create_task(self._work_loop(), name="work"),
        ]

        stopped = asyncio.create_task(self._stopping.wait(), name="stopping")

        try:
            await asyncio.wait([*loops, stopped], return_when=asyncio.FIRST_COMPLETED)
        finally:
            for task in (*loops, stopped):
                task.cancel()

            await asyncio.gather(*loops, stopped, return_exceptions=True)

        _LOG.info("collector.stopped", collector=self._settings.collector_name)

    async def _work_loop(self) -> None:
        while not self._stopping.is_set():
            try:
                await self.work_once()
            except CollectorApiError as error:
                # The API being unreachable is expected and self-healing: the loop waits and asks
                # again, and every job it was holding a lease on goes back to the queue on its own.
                _LOG.warning("collector.lease.failed", error=str(error))

            await self._wait(self._poll_seconds)

    async def work_once(self) -> None:
        """One pass: lease what there is room for, run it, and report it."""
        free = self._settings.capacity - self._running

        if free <= 0:
            return

        batch = await self._api.lease_jobs(min(free, self._max_jobs_per_lease))

        if not batch.jobs:
            return

        _LOG.info("collector.leased", count=len(batch.jobs))

        reports = await asyncio.gather(*(self._run(job) for job in batch.jobs))

        ack = await self._api.submit_results(list(reports))

        # Rejections are logged rather than retried. A rejection means this collector no longer
        # holds the job — its lease expired, or the job never existed — and asking again could
        # only get the same answer.
        if ack.rejected:
            _LOG.warning(
                "collector.results.rejected",
                rejected=[
                    {"jobId": str(item.job_id), "reason": item.reason} for item in ack.rejected
                ],
            )

        _LOG.info(
            "collector.reported",
            accepted=len(ack.accepted),
            duplicates=len(ack.duplicates),
            rejected=len(ack.rejected),
        )

    async def _run(self, job: LeasedJob) -> ResultReport:
        """Run one job under the slot limit and the job timeout."""
        async with self._slots:
            self._running += 1

            try:
                return await self._execute(job)
            finally:
                self._running -= 1

    async def _execute(self, job: LeasedJob) -> ResultReport:
        executor = self._executors.for_kind(job.kind)

        if executor is None:
            # The state every WP-1.3 build is in. It is reported as a failure with a reason
            # rather than left unreported, so the job stops consuming a lease slot every cycle
            # and an operator can see in the queue why nothing is happening.
            _LOG.warning("collector.job.unsupported", jobId=str(job.job_id), kind=job.kind)

            return ResultReport(
                job_id=job.job_id,
                lease_token=job.lease_token,
                outcome=JobOutcome.FAILED,
                detail=f"This collector has no executor for a {job.kind} job ({NO_EXECUTOR}).",
            )

        try:
            # Every device interaction has an explicit timeout (CONVENTIONS.md §5). It is applied
            # here rather than inside each executor, so a hung session cannot wedge the pool
            # however the executor was written.
            async with asyncio.timeout(self._settings.job_timeout_seconds):
                data: dict[str, Any] = await executor.execute(job)
        except TimeoutError:
            _LOG.warning(
                "collector.job.timeout",
                jobId=str(job.job_id),
                kind=job.kind,
                timeoutSeconds=self._settings.job_timeout_seconds,
            )

            return ResultReport(
                job_id=job.job_id,
                lease_token=job.lease_token,
                outcome=JobOutcome.FAILED,
                detail=f"Timed out after {self._settings.job_timeout_seconds:g} seconds.",
            )
        except Exception as error:
            # The message came out of a protocol library talking to a device, which is exactly
            # where a community string ends up. It is redacted before it is logged and again by
            # the API before it is stored (SPEC.md §5).
            detail = redact_text(f"{type(error).__name__}: {error}")

            _LOG.warning("collector.job.failed", jobId=str(job.job_id), kind=job.kind, error=detail)

            return ResultReport(
                job_id=job.job_id,
                lease_token=job.lease_token,
                outcome=JobOutcome.FAILED,
                detail=detail,
            )

        return ResultReport(
            job_id=job.job_id,
            lease_token=job.lease_token,
            outcome=JobOutcome.SUCCEEDED,
            data=data,
        )

    async def _heartbeat_loop(self) -> None:
        while not self._stopping.is_set():
            try:
                ack = await self._api.heartbeat(self._running)

                # The API owns scheduling, so its answer replaces whatever this process was
                # using. A collector cannot end up polling faster than the API wants or holding a
                # lease longer than the API believes it has.
                self._poll_seconds = float(ack.poll_seconds)
                self._max_jobs_per_lease = ack.max_jobs_per_lease
            except CollectorApiError as error:
                _LOG.warning("collector.heartbeat.failed", error=str(error))

            await self._wait(self._settings.heartbeat_seconds)

    async def _wait(self, seconds: float) -> None:
        """Sleep, but wake immediately if the collector has been asked to stop."""
        try:
            async with asyncio.timeout(seconds):
                await self._stopping.wait()
        except TimeoutError:
            return
