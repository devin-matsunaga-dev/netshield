"""Structured logging to stdout, with the secrets taken out at the sink.

CONVENTIONS.md §5 says structlog to stdout as JSON and no ``print``. SPEC.md §5 says no
credential ever reaches a log. The second is enforced here, in a processor every line passes
through, rather than at each call site — the same decision WP-0.3 made for the .NET side, and for
the same reason: a rule that depends on every author remembering it is a rule that will be
forgotten exactly once.
"""

from __future__ import annotations

import logging
import re
import sys
from collections.abc import Mapping, MutableMapping
from typing import Any, Final

import structlog
from pydantic import SecretStr

PLACEHOLDER: Final = "[REDACTED]"

_SECRET_KEY: Final = re.compile(
    r"pass(word|wd|phrase)|pwd|secret|token|api[-_]?key|credential|community|private[-_]?key"
    r"|authorization|cookie|kek|dek",
    re.IGNORECASE,
)
"""A key whose value must never be written.

Kept deliberately in step with ``NetShield.Platform.Logging.SecretRedactor`` and deliberately
restated rather than shared: the two processes have no code in common, and the rule has to hold
independently in each.
"""

_BEARER: Final = re.compile(r"Bearer\s+\S+", re.IGNORECASE)
_PEM_BLOCK: Final = re.compile(
    r"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----",
    re.DOTALL,
)
_KEYED: Final = re.compile(
    r"(?P<key>pass(?:word|wd|phrase)|pwd|secret|token|community|api[-_]?key)"
    r"(?P<separator>\s*[=:]\s*)(?P<value>\S+)",
    re.IGNORECASE,
)

_MAX_DEPTH: Final = 6
"""How far into a nested value the redactor walks before it stops describing and blanks."""


def redact_text(text: str) -> str:
    """Take the recognisable secret shapes out of free text.

    Device error messages and exception strings are where a community string or a URL with a
    password in it ends up, and neither is something the call site thought of as a secret.
    """
    redacted = _PEM_BLOCK.sub(PLACEHOLDER, text)
    redacted = _BEARER.sub(f"Bearer {PLACEHOLDER}", redacted)
    return _KEYED.sub(
        lambda match: f"{match.group('key')}{match.group('separator')}{PLACEHOLDER}",
        redacted,
    )


def redact_value(key: str, value: Any, depth: int = 0) -> Any:
    """Redact one logged value, by its key and then by its shape."""
    if _SECRET_KEY.search(key):
        return PLACEHOLDER

    if isinstance(value, SecretStr):
        # Pydantic already masks these in a repr; blanking here means a caller that reached for
        # get_secret_value() on the way into a log line does not get to undo that.
        return PLACEHOLDER

    if depth >= _MAX_DEPTH:
        return PLACEHOLDER

    if isinstance(value, str):
        return redact_text(value)

    if isinstance(value, Mapping):
        return {
            str(nested_key): redact_value(str(nested_key), nested_value, depth + 1)
            for nested_key, nested_value in value.items()
        }

    if isinstance(value, list | tuple | set | frozenset):
        return [redact_value(key, item, depth + 1) for item in value]

    return value


def redaction_processor(
    _logger: object,
    _method: str,
    event: MutableMapping[str, Any],
) -> MutableMapping[str, Any]:
    """The structlog processor that applies the two rules above to every line."""
    return {str(key): redact_value(str(key), value) for key, value in event.items()}


def configure_logging(level: str = "INFO") -> None:
    """Send JSON to stdout, redacted, at ``level`` and above.

    stdout rather than a file: the collector runs under an orchestrator that captures it, and a
    log file is a place a credential could be left behind on disk if the redactor ever missed
    one.
    """
    logging.basicConfig(format="%(message)s", stream=sys.stdout, level=level.upper())

    structlog.configure(
        processors=[
            structlog.contextvars.merge_contextvars,
            structlog.processors.add_log_level,
            structlog.processors.TimeStamper(fmt="iso", utc=True),
            structlog.processors.StackInfoRenderer(),
            structlog.processors.format_exc_info,
            # Last before the renderer, so that nothing added by a processor above can get past
            # it and nothing below it can put a secret back.
            redaction_processor,
            structlog.processors.JSONRenderer(),
        ],
        wrapper_class=structlog.make_filtering_bound_logger(
            logging.getLevelNamesMapping()[level.upper()]
        ),
        logger_factory=structlog.PrintLoggerFactory(file=sys.stdout),
        cache_logger_on_first_use=True,
    )
