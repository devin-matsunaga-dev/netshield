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
from collector.discovery import DiscoverExecutor, RangeSweepExecutor
from collector.icmp import IcmpExecutor
from collector.jobs import ExecutorRegistry
from collector.logging import configure_logging
from collector.runner import CollectorRunner
from collector.snmp import (
    ClientWalkExecutor,
    NeighborWalkExecutor,
    RouteWalkExecutor,
    SnmpWalkExecutor,
)
from collector.vendors import VendorRegistry, snmp_adapters

_LOG: Final = structlog.get_logger(__name__)


def build_registries() -> tuple[ExecutorRegistry, VendorRegistry]:
    """The two seams: the executors this build can run, and the vendors it recognises.

    ``IcmpExecutor`` answers for ``Poll`` jobs whose parameters name the ICMP probe, which is
    every job the reachability schedule queues. ``DiscoverExecutor`` answers for ``Discover``
    jobs and dispatches on ``parameters.walk`` to one of the five walks a ``Discover`` can be:
    the SNMP fingerprint of a device an on-demand walk asked for, the range sweep a discovery run
    queued, the read of a device's ARP and forwarding tables the client schedule queued, the read
    of its neighbour protocols, or the read of its routing table. A job of a kind or a walk this
    build cannot run is reported as a failure naming the reason rather than dropped.

    Two walks are handed the vendor registry and three are not. ICMP needs no adapter — an echo
    request is the same question whoever made the box — and neither does the sweep. Nor does the
    client walk, nor the route walk: IP-MIB, BRIDGE-MIB and IP-FORWARD-MIB are the same
    everywhere, which is exactly why client tracking and L3 adjacency work on a device NetShield
    cannot otherwise identify. What differs by vendor is which *private* MIB is worth reading —
    the fingerprint asks that of the scalars, and the neighbour walk asks it of CDP, which is
    Cisco's protocol in Cisco's MIB and is declared by the two Cisco adapters alone.
    """
    vendors = VendorRegistry(snmp_adapters())

    discover = DiscoverExecutor(
        [
            SnmpWalkExecutor(vendors),
            RangeSweepExecutor(),
            ClientWalkExecutor(),
            NeighborWalkExecutor(vendors),
            RouteWalkExecutor(),
        ]
    )

    return ExecutorRegistry([IcmpExecutor(), discover]), vendors


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
