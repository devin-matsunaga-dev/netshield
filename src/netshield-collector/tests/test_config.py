"""The environment is the whole of the collector's configuration."""

from __future__ import annotations

import pytest
from pydantic import ValidationError

from collector.config import CollectorSettings


def test_settings_read_from_the_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("NETSHIELD_API_URL", "http://api.example:8080")
    monkeypatch.setenv("NETSHIELD_COLLECTOR_SECRET", "a" * 48)
    monkeypatch.setenv("NETSHIELD_COLLECTOR_NAME", "collector-1")

    settings = CollectorSettings()  # type: ignore[call-arg]

    assert settings.collector_name == "collector-1"
    assert str(settings.api_url).startswith("http://api.example:8080")


def test_missing_secret_refuses_to_start(monkeypatch: pytest.MonkeyPatch) -> None:
    """There is no default and no development fallback for either secret-bearing value."""
    monkeypatch.setenv("NETSHIELD_API_URL", "http://api.example")
    monkeypatch.setenv("NETSHIELD_COLLECTOR_NAME", "collector-1")
    monkeypatch.delenv("NETSHIELD_COLLECTOR_SECRET", raising=False)

    with pytest.raises(ValidationError):
        CollectorSettings()  # type: ignore[call-arg]


def test_the_secret_is_not_in_the_repr(settings: CollectorSettings) -> None:
    """A settings object that got logged must not be how the shared secret escapes."""
    from tests.conftest import SHARED_SECRET

    assert SHARED_SECRET not in repr(settings)
    assert SHARED_SECRET not in str(settings)
