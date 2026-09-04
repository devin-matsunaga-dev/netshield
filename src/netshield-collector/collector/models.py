"""The internal collector contract, as this side of it sees it (ARCHITECTURE.md §7).

These mirror the shapes ``NetShield.Inventory.Collector.Contract`` writes. They are a second,
hand-written copy rather than something generated, because the contract is not in the OpenAPI
document that the SPA's client is generated from — and it is not in that document on purpose: a
leased job carries an opened device credential, and that shape must never appear in a contract a
browser is built from.

Every secret member is a ``SecretStr``. Pydantic then masks it in a repr and in a model dump, so
the shortest path to a credential in a log line — logging the model — produces nothing.
"""

from __future__ import annotations

from datetime import datetime
from enum import StrEnum
from typing import Any
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, SecretStr


class JobKind(StrEnum):
    """What the API is asking for. The three from ARCHITECTURE.md §7, and no write among them."""

    POLL = "Poll"
    DISCOVER = "Discover"
    CONFIG_FETCH = "ConfigFetch"


class JobOutcome(StrEnum):
    """What the collector reports back."""

    SUCCEEDED = "Succeeded"
    FAILED = "Failed"


class CredentialKind(StrEnum):
    """Which protocol a credential authenticates."""

    SNMP_V2C = "SnmpV2c"
    SNMP_V3 = "SnmpV3"
    SSH_PASSWORD = "SshPassword"  # noqa: S105 - a credential kind, not a credential
    SSH_KEY = "SshKey"


class WireModel(BaseModel):
    """The shared configuration: camelCase on the wire, snake_case in Python."""

    model_config = ConfigDict(
        populate_by_name=True,
        alias_generator=lambda name: "".join(
            part if index == 0 else part.capitalize() for index, part in enumerate(name.split("_"))
        ),
        frozen=True,
    )


class CredentialMaterial(WireModel):
    """The plaintext of a credential. In memory only, for the life of one job."""

    community: SecretStr | None = None
    auth_password: SecretStr | None = None
    privacy_password: SecretStr | None = None
    password: SecretStr | None = None
    private_key: SecretStr | None = None
    private_key_password: SecretStr | None = None


class JobCredential(WireModel):
    """The credential a job is to be run with, and the profile it came from."""

    credential_profile_id: UUID
    kind: CredentialKind
    username: str | None = None
    auth_algorithm: str | None = None
    privacy_algorithm: str | None = None
    material: CredentialMaterial


class JobDevice(WireModel):
    """The device a job is about, in the facts needed to reach it."""

    device_id: UUID
    hostname: str
    ip_address: str
    vendor: str


class LeasedJob(WireModel):
    """One job, claimed by this collector until ``lease_expires_at``."""

    job_id: UUID
    kind: JobKind
    lease_token: str
    lease_expires_at: datetime
    attempt: int
    device: JobDevice | None = None
    parameters: dict[str, Any] | None = None
    credential: JobCredential | None = None


class JobBatch(WireModel):
    """The answer to a lease call."""

    jobs: list[LeasedJob] = Field(default_factory=list)
    lease_seconds: int


class ResultReport(WireModel):
    """What this collector says about one job it was leased."""

    job_id: UUID
    lease_token: str
    outcome: JobOutcome
    detail: str | None = None
    data: dict[str, Any] | None = None


class ResultsRequest(WireModel):
    """A batch of reports."""

    collector: str
    results: list[ResultReport]


class RejectedResult(WireModel):
    """One report the API would not apply, and why."""

    job_id: UUID
    reason: str


class ResultsAck(WireModel):
    """What the API did with each report."""

    accepted: list[UUID] = Field(default_factory=list)
    duplicates: list[UUID] = Field(default_factory=list)
    rejected: list[RejectedResult] = Field(default_factory=list)


class HeartbeatRequest(WireModel):
    """This collector's claim about itself."""

    name: str
    version: str | None = None
    capacity: int
    running: int


class HeartbeatAck(WireModel):
    """The pacing the API wants this collector to adopt."""

    acknowledged_at: datetime
    poll_seconds: int
    lease_seconds: int
    max_jobs_per_lease: int
