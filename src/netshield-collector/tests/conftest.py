"""Fixtures shared by the collector suite.

CONVENTIONS.md §7: protocol interactions are tested against recorded fixtures, never a live
device. Two kinds of fixture live here. The API side of the contract is mocked at the transport
with ``respx``. The device side is a recorded walk — a flat mapping of OID to decoded value,
replayed through ``FixtureSession`` — which is what every vendor's fingerprint is proved against;
see ``tests/fixtures/snmp/README.md``.
"""

from __future__ import annotations

import json
from collections.abc import Iterator
from datetime import UTC, datetime
from pathlib import Path
from uuid import UUID, uuid4

import pytest
import respx

from collector.config import CollectorSettings
from collector.models import (
    CredentialKind,
    JobCredential,
    JobDevice,
    JobKind,
    LeasedJob,
)
from collector.snmp.session import FixtureSession

API_URL = "http://api.test"

WALKS = Path(__file__).parent / "fixtures" / "snmp"
"""Where the recorded walks live."""

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


def walk_fixture(name: str) -> dict[str, str]:
    """One recorded walk's values, by file stem."""
    document = json.loads((WALKS / f"{name}.json").read_text())
    values = document["values"]

    assert isinstance(values, dict)

    return {str(oid): str(value) for oid, value in values.items()}


def walk_session(name: str) -> FixtureSession:
    """A session replaying one recorded walk."""
    return FixtureSession(walk_fixture(name))


def snmp_credential(
    *,
    kind: CredentialKind = CredentialKind.SNMP_V2C,
    community: str | None = "fixture-community",
    username: str | None = None,
    auth_algorithm: str | None = None,
    privacy_algorithm: str | None = None,
    auth_password: str | None = None,
    privacy_password: str | None = None,
) -> JobCredential:
    """A credential of the shape a lease delivers. Every value is recognisably a fixture."""
    return JobCredential.model_validate(
        {
            "credentialProfileId": str(uuid4()),
            "kind": kind.value,
            "username": username,
            "authAlgorithm": auth_algorithm,
            "privacyAlgorithm": privacy_algorithm,
            "material": {
                "community": community,
                "authPassword": auth_password,
                "privacyPassword": privacy_password,
            },
        }
    )


def walk_job(
    *,
    parameters: dict[str, object] | None = None,
    credential: JobCredential | None = None,
    device: JobDevice | None = None,
    address: str = "192.0.2.10",
) -> LeasedJob:
    """A leased Discover job carrying the SNMP walk's parameters."""
    return LeasedJob(
        job_id=uuid4(),
        kind=JobKind.DISCOVER,
        lease_token="lease-token",
        lease_expires_at=datetime.now(UTC),
        attempt=1,
        device=device
        if device is not None
        else JobDevice.model_validate(
            {
                "deviceId": str(uuid4()),
                "hostname": "lab-sw-01",
                "ipAddress": address,
                "vendor": "Unknown",
            }
        ),
        parameters=parameters
        if parameters is not None
        else {
            "walk": "snmp",
            "timeoutSeconds": 2.0,
            "retries": 1,
            "maxRepetitions": 25,
            "maxRows": 5000,
            "maxInterfaces": 500,
        },
        credential=credential if credential is not None else snmp_credential(),
    )
