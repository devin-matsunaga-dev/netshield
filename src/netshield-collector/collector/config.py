"""Where the collector's settings come from: the environment, and nothing else."""

from __future__ import annotations

from pydantic import Field, HttpUrl, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class CollectorSettings(BaseSettings):
    """Everything the collector needs to start, read from ``NETSHIELD_*`` environment variables.

    There is no settings file and no default for either secret-bearing value. A collector that
    has not been told where the API is, or what secret to present, fails at startup with a
    message naming the variable rather than starting and failing every request.

    The pacing values here are only what the collector uses before its first heartbeat is
    answered. From then on the API's acknowledgement supplies the poll interval, the lease
    duration and the batch ceiling, because ARCHITECTURE.md §7 puts scheduling on the API and a
    collector carrying its own copy of those numbers is a collector that can drift out of step
    with the server it reports to.
    """

    model_config = SettingsConfigDict(
        env_prefix="NETSHIELD_",
        env_file=None,
        extra="ignore",
        frozen=True,
    )

    api_url: HttpUrl
    """Where the API is. Supplied by the orchestrator; never written into this repository."""

    collector_secret: SecretStr
    """The shared secret presented as a bearer token on every call (ARCHITECTURE.md §7)."""

    collector_name: str = Field(min_length=1, max_length=128)
    """What this collector calls itself. Stable across its restarts — the API keys its row on it."""

    request_timeout_seconds: float = Field(default=30.0, gt=0, le=300)
    """How long any single call to the API may take. CONVENTIONS.md §5 admits no unbounded wait."""

    job_timeout_seconds: float = Field(default=120.0, gt=0, le=3600)
    """
    How long one job may run before it is abandoned as failed.

    A hung session must never wedge the worker pool (CONVENTIONS.md §5), so this is enforced
    around every execution rather than trusted to whatever protocol library is doing the work.
    It should stay comfortably below the API's lease duration: a job that overran its lease has
    already been given to somebody else, and finishing it would only produce a result the API
    refuses.
    """

    max_attempts: int = Field(default=4, ge=1, le=10)
    """How many times one API call is tried before the loop gives up on this pass."""

    backoff_seconds: float = Field(default=1.0, gt=0, le=60)
    """The base of the exponential, jittered backoff between those attempts."""

    capacity: int = Field(default=8, ge=1, le=512)
    """How many jobs this collector will run at once. It leases no more than it has room for."""

    poll_seconds: float = Field(default=15.0, gt=0, le=300)
    """How often to ask for work, until the API's first heartbeat acknowledgement says otherwise."""

    heartbeat_seconds: float = Field(default=15.0, gt=0, le=300)
    """How often to report liveness."""

    log_level: str = Field(default="INFO")
    """The lowest level written to stdout."""
