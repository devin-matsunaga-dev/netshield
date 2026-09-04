using System.Text.RegularExpressions;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The two structural promises ARCHITECTURE.md §7 makes about <c>netshield-collector</c>: it
/// holds no database credential and never touches PostgreSQL, and it never writes to a device.
/// </summary>
/// <remarks>
/// Checked over the collector's own files, the way every other rule in this project is checked —
/// as text on disk, so a rule holds for the file that has not been compiled or imported yet. The
/// first promise is kept by the collector having nothing to connect with, which is a statement
/// about its dependency list; the second by there being no write primitive anywhere in it.
/// </remarks>
public sealed partial class CollectorIsolationTests
{
    private static readonly string CollectorRoot = Path.Combine(Repository.Root, "src", "netshield-collector");

    [Fact]
    public void TheCollector_DeclaresNoDatabaseDriver()
    {
        string manifest = File.ReadAllText(Path.Combine(CollectorRoot, "pyproject.toml"));

        IReadOnlyList<string> drivers =
        [
            .. from driver in (string[])
                   ["psycopg", "asyncpg", "sqlalchemy", "pg8000", "redis", "aioredis"]
               where manifest.Contains(driver, StringComparison.OrdinalIgnoreCase)
               select driver
        ];

        drivers.Should().BeEmpty(
            "ARCHITECTURE.md §7: the collector holds no database credential and never touches "
            + "PostgreSQL, and the surest way to keep that true is for it to have nothing to "
            + "connect with");
    }

    [Fact]
    public void TheCollector_ReadsNoConnectionStringFromItsEnvironment()
    {
        IReadOnlyList<string> offenders =
        [
            .. from file in PythonFiles
               let source = File.ReadAllText(file)
               where ConnectionShape().IsMatch(source)
               select Repository.RelativeToRoot(file)
        ];

        offenders.Should().BeEmpty(
            "the collector is given the API address, a shared secret and its own name, and "
            + "nothing that opens a store (SPEC.md §5)");
    }

    [Fact]
    public void TheCollector_ContainsNoWritePrimitive()
    {
        // SPEC.md §3 and ARCHITECTURE.md §1: no SNMP set, no configuration mode, no config push.
        //
        // The list gained "set_cmd" and "snmpset" in WP-1.5, which is the package that brought
        // the first protocol library: pysnmp exports its write primitive as `set_cmd` from the
        // very module the collector imports `get_cmd` and `bulk_cmd` from, and the guess this
        // rule was written with — the camelCase spelling of an older release — would not have
        // caught it. A rule that names the wrong symbol is worse than no rule, because it reads
        // as cover.
        //
        // It is a text scan, so it cannot tell code from prose: a comment in the collector that
        // named one of these would fail it too. That is deliberate and the collector's own
        // comments are written around it.
        IReadOnlyList<string> offenders =
        [
            .. from file in PythonFiles
               let source = File.ReadAllText(file)
               from forbidden in (string[])[
                   "set_cmd", "setCmd", "snmpset", "snmp_set",
                   "config_set", "send_config", "write_memory"]
               where source.Contains(forbidden, StringComparison.Ordinal)
               select $"{Repository.RelativeToRoot(file)} mentions {forbidden}"
        ];

        offenders.Should().BeEmpty("NetShield never writes to a network device, in any package");
    }

    [Fact]
    public void TheCollector_UsesNoPrintStatement()
    {
        // CONVENTIONS.md §5: structlog to stdout as JSON, and no print. ruff enforces it inside
        // the collector's own gate; this says it out loud in the gate everyone runs.
        IReadOnlyList<string> offenders =
        [
            .. from file in PythonFiles
               where PrintCall().IsMatch(File.ReadAllText(file))
               select Repository.RelativeToRoot(file)
        ];

        offenders.Should().BeEmpty("a print is a log line nothing can redact");
    }

    private static IReadOnlyList<string> PythonFiles { get; } =
        Directory.Exists(CollectorRoot)
            ? [.. Directory.EnumerateFiles(CollectorRoot, "*.py", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)]
            : [];

    /// <summary>A store URI or an ADO-style keyword, the same shapes <c>ConfigurationTests</c> looks for.</summary>
    [GeneratedRegex(
        """(postgres|postgresql|redis|amqp)://|\b(psycopg|asyncpg|create_engine)\b""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionShape();

    /// <summary>A call to the built-in <c>print</c>, not a member called <c>print</c>.</summary>
    [GeneratedRegex(@"(?<![\w.])print\s*\(")]
    private static partial Regex PrintCall();
}
