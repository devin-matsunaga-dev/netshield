"""SPEC.md §5 at the collector's sink: no credential reaches a log line."""

from __future__ import annotations

from pydantic import SecretStr

from collector.logging import PLACEHOLDER, redact_text, redact_value, redaction_processor
from collector.models import CredentialKind, CredentialMaterial, JobCredential


def test_a_secret_key_is_blanked_whatever_the_value() -> None:
    assert redact_value("community", "public") == PLACEHOLDER
    assert redact_value("privateKey", "-----BEGIN RSA PRIVATE KEY-----") == PLACEHOLDER
    assert redact_value("authPassword", "hunter2") == PLACEHOLDER


def test_an_ordinary_key_survives() -> None:
    assert redact_value("hostname", "core-sw-1") == "core-sw-1"
    assert redact_value("attempt", 3) == 3


def test_a_secretstr_is_blanked_even_when_the_key_looks_innocent() -> None:
    assert redact_value("value", SecretStr("hunter2")) == PLACEHOLDER


def test_a_bearer_token_is_taken_out_of_free_text() -> None:
    redacted = redact_text("401 from GET /jobs with Authorization: Bearer abc.def.ghi")

    assert "abc.def.ghi" not in redacted
    assert PLACEHOLDER in redacted


def test_a_pem_block_is_taken_out_of_free_text() -> None:
    key = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNza\n-----END OPENSSH PRIVATE KEY-----"

    assert "b3BlbnNza" not in redact_text(f"ssh failed with key {key}")


def test_a_keyed_secret_is_taken_out_of_a_device_error() -> None:
    """The shape a protocol library actually produces: a message quoting what it was given."""
    redacted = redact_text("authentication failed (community=s3cr3t)")

    assert "s3cr3t" not in redacted


def test_nested_structures_are_walked() -> None:
    event = redaction_processor(
        None,
        "info",
        {
            "event": "collector.job.failed",
            "job": {"device": {"hostname": "core-sw-1"}, "credential": {"password": "hunter2"}},
        },
    )

    assert event["job"]["device"]["hostname"] == "core-sw-1"
    assert event["job"]["credential"] == PLACEHOLDER


def test_a_whole_credential_model_survives_being_logged() -> None:
    """The mistake this has to survive is somebody logging the job they were handed."""
    credential = JobCredential(
        credential_profile_id="00000000-0000-0000-0000-000000000001",  # type: ignore[arg-type]
        kind=CredentialKind.SSH_PASSWORD,
        username="netshield-ro",
        material=CredentialMaterial(password=SecretStr("hunter2")),
    )

    event = redaction_processor(None, "info", {"credential": credential.model_dump()})

    assert "hunter2" not in repr(event)


def test_pydantic_masks_the_material_in_a_repr() -> None:
    material = CredentialMaterial(community=SecretStr("public"))

    assert "public" not in repr(material)
