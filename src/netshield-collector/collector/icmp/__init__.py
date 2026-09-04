"""ICMP reachability: one protocol, one package (CONVENTIONS.md §5).

``packet`` is the RFC layer and knows nothing about sockets; ``probe`` is the socket layer and
knows nothing about jobs; ``executor`` is the job layer and knows nothing about bytes. Nothing
here writes to a device — an echo request asks a question and carries no instruction, which is
the only kind of traffic NetShield ever sends (ARCHITECTURE.md §1).

Only the executor is re-exported. The ``probe`` function stays reachable as
``collector.icmp.probe.probe`` and is deliberately not lifted to this level: a package attribute
that shadows one of its own submodules makes ``collector.icmp.probe`` mean two different things
depending on how it was imported, which is a trap for the next reader and for anything that wants
to reach past the function into the module it lives in.
"""

from collector.icmp.executor import IcmpExecutor, IcmpJobError, IcmpJobParameters

__all__ = ["IcmpExecutor", "IcmpJobError", "IcmpJobParameters"]
