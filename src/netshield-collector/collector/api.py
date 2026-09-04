"""The client for the internal collector contract.

One class, three calls, and the retry policy that makes the collector's loop safe to run on a
network that is sometimes not there. CONVENTIONS.md §5 names ``httpx.AsyncClient`` with retry and
jittered backoff; this is that.
"""

from __future__ import annotations

import asyncio
import secrets
from types import TracebackType
from typing import Final, Self

import httpx
import structlog

from collector.config import CollectorSettings
from collector.models import (
    HeartbeatAck,
    HeartbeatRequest,
    JobBatch,
    ResultReport,
    ResultsAck,
    ResultsRequest,
)

_LOG: Final = structlog.get_logger(__name__)

_JOBS_PATH: Final = "/internal/collector/jobs"
_RESULTS_PATH: Final = "/internal/collector/results"
_HEARTBEAT_PATH: Final = "/internal/collector/heartbeat"

_RETRYABLE_STATUS: Final = frozenset({408, 429, 500, 502, 503, 504})
"""Statuses worth trying again.

Everything else in the 4xx range is the API saying this request is wrong, and a request that is
wrong is wrong however many times it is sent — retrying one only costs a round trip and delays
the log line that says what happened.
"""


class CollectorApiError(RuntimeError):
    """A call to the API did not succeed, after every attempt it was allowed."""


class CollectorApi:
    """Talks to the NetShield API on behalf of one collector."""

    def __init__(
        self,
        settings: CollectorSettings,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._settings = settings
        self._client = client or httpx.AsyncClient(
            base_url=str(settings.api_url).rstrip("/"),
            timeout=settings.request_timeout_seconds,
            headers={
                # The shared secret, as a bearer token (ARCHITECTURE.md §8). It is read out of
                # the SecretStr exactly here, on the way into the header, and nowhere else.
                "Authorization": f"Bearer {settings.collector_secret.get_secret_value()}",
                "Accept": "application/json",
            },
        )

    async def __aenter__(self) -> Self:
        return self

    async def __aexit__(
        self,
        exception_type: type[BaseException] | None,
        exception: BaseException | None,
        traceback: TracebackType | None,
    ) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        """Close the underlying connection pool."""
        await self._client.aclose()

    async def lease_jobs(self, limit: int) -> JobBatch:
        """Ask for up to ``limit`` jobs.

        The API decides how many it actually hands over, and an empty batch is the normal answer
        when there is nothing due.
        """
        response = await self._send(
            "GET",
            _JOBS_PATH,
            params={"collector": self._settings.collector_name, "limit": limit},
        )

        return JobBatch.model_validate(response.json())

    async def submit_results(self, results: list[ResultReport]) -> ResultsAck:
        """Report on jobs this collector was leased.

        Safe to repeat. The API is idempotent by job id and lease token, so a submission that was
        made but whose answer was never seen can simply be made again.
        """
        request = ResultsRequest(collector=self._settings.collector_name, results=results)

        response = await self._send(
            "POST",
            _RESULTS_PATH,
            json=request.model_dump(mode="json", by_alias=True, exclude_none=True),
        )

        return ResultsAck.model_validate(response.json())

    async def heartbeat(self, running: int) -> HeartbeatAck:
        """Report liveness and capacity, and receive the pacing the API wants."""
        from collector import __version__

        request = HeartbeatRequest(
            name=self._settings.collector_name,
            version=__version__,
            capacity=self._settings.capacity,
            running=running,
        )

        response = await self._send(
            "POST",
            _HEARTBEAT_PATH,
            json=request.model_dump(mode="json", by_alias=True),
        )

        return HeartbeatAck.model_validate(response.json())

    async def _send(self, method: str, path: str, **kwargs: object) -> httpx.Response:
        """One call, retried with exponential and jittered backoff.

        Jitter matters more here than it looks. Every collector in a fleet polls on the same
        interval, so an API restart would otherwise have all of them retrying in lockstep at the
        moment it comes back — which is the thundering herd the backoff exists to avoid.
        """
        last_error: Exception | None = None

        for attempt in range(1, self._settings.max_attempts + 1):
            try:
                response = await self._client.request(method, path, **kwargs)  # type: ignore[arg-type]

                if response.status_code < 400:
                    return response

                if response.status_code not in _RETRYABLE_STATUS:
                    raise CollectorApiError(
                        f"{method} {path} was refused with {response.status_code}."
                    )

                last_error = CollectorApiError(f"{method} {path} answered {response.status_code}.")
            except httpx.HTTPError as error:
                # The message is not interpolated into the log line as text; it is a field, so
                # the redaction processor sees it as a value and can take a token out of it.
                last_error = error

            if attempt < self._settings.max_attempts:
                delay = self._backoff(attempt)

                _LOG.warning(
                    "collector.api.retrying",
                    method=method,
                    path=path,
                    attempt=attempt,
                    delay_seconds=round(delay, 3),
                    error=str(last_error),
                )

                await asyncio.sleep(delay)

        raise CollectorApiError(
            f"{method} {path} failed after {self._settings.max_attempts} attempts."
        ) from last_error

    def _backoff(self, attempt: int) -> float:
        """Exponential from the configured base, with full jitter."""
        ceiling = self._settings.backoff_seconds * float(2 ** (attempt - 1))

        # secrets rather than random: this is not a security decision, but ruff is right that a
        # module named random has no business in a service, and the cost here is nothing.
        return ceiling * (secrets.randbelow(1000) / 1000)
