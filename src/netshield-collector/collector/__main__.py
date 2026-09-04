"""The entry point: ``python -m collector``.

It reads the environment, configures logging, and runs the loop until the orchestrator asks it to
stop. Nothing is decided here that is not a startup concern.
"""

from __future__ import annotations

import asyncio
import signal
import sys
from typing import Final

import structlog
from pydantic import ValidationError

from collector.api import CollectorApi
from collector.config import CollectorSettings
from collector.icmp import IcmpExecutor
from collector.jobs import ExecutorRegistry
from collector.logging import configure_logging
from collector.runner import CollectorRunner
from collector.vendors import VendorRegistry

_LOG: Final = structlog.get_logger(__name__)


def build_registries() -> tuple[ExecutorRegistry, VendorRegistry]:
    """The two seams: one executor, and no vendor adapters yet.

    ``IcmpExecutor`` answers for ``Poll`` jobs whose parameters name the ICMP probe, which is
    every job the reachability schedule queues. WP-1.5 registers the SNMP executor and the vendor
    adapters behind it; a job of a kind or a probe this build cannot run is still reported as a
    failure naming the reason rather than dropped.

    ICMP needs no vendor adapter and asks the registry for none — an echo request is the same
    question whoever made the box.
    """
    return ExecutorRegistry([IcmpExecutor()]), VendorRegistry()


async def main() -> int:
    """Run the collector. Returns the process exit code."""
    try:
        settings = CollectorSettings()  # type: ignore[call-arg]
    except ValidationError as error:
        # Logging is not configured yet and this is the one message that has to reach an operator
        # whatever happens, so it goes to stderr directly. It names the variables that are
        # missing and no value of any of them.
        sys.stderr.write(f"netshield-collector cannot start: {error}\n")

        return 1

    configure_logging(settings.log_level)

    executors, vendors = build_registries()

    if len(vendors) == 0:
        _LOG.info("collector.vendors.none-registered")

    async with CollectorApi(settings) as api:
        runner = CollectorRunner(api, executors, settings)

        loop = asyncio.get_running_loop()

        # SIGTERM is how a container is asked to go away, and SIGINT is Ctrl-C. Both mean finish
        # the pass and return: a job left in flight simply has its lease expire and is given to
        # somebody else, which is better than a half-written result.
        for received in (signal.SIGTERM, signal.SIGINT):
            loop.add_signal_handler(received, runner.stop)

        await runner.run()

    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
