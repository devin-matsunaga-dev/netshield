"""Fixtures shared by the collector suite.

CONVENTIONS.md §7: protocol interactions are tested against recorded fixtures, never a live
device. There is no device here at all yet — WP-1.3 has no protocol library — so what these
fixtures record is the other side of the contract: the NetShield API, mocked at the transport
with ``respx``.
"""

from __future__ import annotations

from collections.abc import Iterator
from datetime import UTC, datetime
from uuid import UUID, uuid4

import pytest
import respx

from collector.config import CollectorSettings
from collector.models import JobKind, LeasedJob

API_URL = "http://api.test"

# Recognisably not a real secret, and long enough to satisfy the API's own floor.
SHARED_SECRET = "test-shared-secret-0000000000000000000000000000"


@pytest.fixture
def settings() -> CollectorSettings:
    """Settings a test can rely on: fast, small, and pointed at the mocked API."""
    return CollectorSettings(
        api_url=API_URL,  # type: ignore[arg-type]
        collector_secret=SHARED_SECRET,  # type: ignore[arg-type]
        collector_name="collector-test",
        request_timeout_seconds=1.0,
        job_timeout_seconds=0.5,
        max_attempts=3,
        backoff_seconds=0.001,
        capacity=4,
        poll_seconds=0.01,
        heartbeat_seconds=0.01,
    )


@pytest.fixture
def api_mock() -> Iterator[respx.MockRouter]:
    """The API, mocked at the transport."""
    with respx.mock(base_url=API_URL, assert_all_called=False) as mock:
        yield mock


def leased_job(
    *,
    job_id: UUID | None = None,
    kind: JobKind = JobKind.POLL,
    token: str = "lease-token",
) -> LeasedJob:
    """A leased job with no device and no credential — the shape a test can build cheaply."""
    return LeasedJob(
        job_id=job_id or uuid4(),
        kind=kind,
        lease_token=token,
        lease_expires_at=datetime.now(UTC),
        attempt=1,
    )
