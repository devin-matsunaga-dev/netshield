"""The other seam: what actually runs a job of a given kind.

A ``JobExecutor`` is per protocol — ICMP in WP-1.4, SNMP in WP-1.5, SSH in Phase 7 — and consults
the vendor registry for whatever is vendor-specific. WP-1.3 registers none, which is the state
the runner has to behave correctly in: an unregistered kind is reported as a failure naming the
reason, not retried for ever and not silently dropped.
"""

from __future__ import annotations

from typing import Any, Protocol, runtime_checkable

from collector.models import JobKind, LeasedJob

NO_EXECUTOR = "no-executor"
"""The failure detail for a job whose kind this build cannot run."""


@runtime_checkable
class JobExecutor(Protocol):
    """Runs one kind of job."""

    kind: JobKind
    """Which kind this executor is for."""

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Do the work and return what was found, as the shape that kind's package defines.

        It is handed the whole leased job, credential included, and must not write any part of it
        anywhere (ARCHITECTURE.md §7). Raising is how it reports a failure; the runner turns an
        exception into a failed result with a redacted message, so an implementation does not
        have to remember to.
        """
        ...


class ExecutorRegistry:
    """Finds the executor for a job kind."""

    def __init__(self, executors: list[JobExecutor] | None = None) -> None:
        self._executors: dict[JobKind, JobExecutor] = {
            executor.kind: executor for executor in (executors or [])
        }

    def register(self, executor: JobExecutor) -> None:
        """Adds one kind's executor. Registering a kind twice is a mistake, not a merge."""
        if executor.kind in self._executors:
            raise ValueError(f"An executor for {executor.kind} is already registered.")

        self._executors[executor.kind] = executor

    def for_kind(self, kind: JobKind) -> JobExecutor | None:
        """The executor for ``kind``, or ``None`` when this build cannot run it."""
        return self._executors.get(kind)

    def __len__(self) -> int:
        return len(self._executors)
