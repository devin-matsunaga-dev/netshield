using System.Text.RegularExpressions;

using FluentAssertions;

using NetShield.Contracts.Collector;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The discriminators that let two kinds of work share one collector job kind, written down
/// twice.
/// </summary>
/// <remarks>
/// <para>
/// WP-1.4 settled that a <c>Poll</c> carries a <c>probe</c> naming which probe to run, and WP-1.5
/// that a <c>Discover</c> carries a <c>walk</c>. WP-1.6 is the package that made the second one
/// matter: a fingerprint walk and a range sweep are both <c>Discover</c> jobs, they sit in the
/// same table looking identical, and the only thing telling them apart is a string this
/// repository writes and <c>netshield-collector</c> reads.
/// </para>
/// <para>
/// There is no generator between the two — the collector contract is deliberately absent from
/// the OpenAPI document (WP-1.3) — so the failure mode without this test is quiet and bad: a
/// value renamed on one side leaves the API queueing work no collector recognises, or a collector
/// answering a question the API will not read. The vendor lists have the same shape of problem
/// and <see cref="VendorParityTests"/> is the same answer to it.
/// </para>
/// </remarks>
public sealed class JobDiscriminatorParityTests
{
    private static readonly string CollectorRoot =
        Path.Combine(Repository.Root, "src", "netshield-collector", "collector");

    [Fact]
    public void TheSnmpWalkDiscriminator_IsTheSameOnBothSides()
    {
        Constant("snmp/executor.py", "WALK_NAME").Should().Be(
            ApiConstant("Discovery/SnmpWalkParameters.cs", "WalkName"),
            "a Discover job naming this walk is what the fingerprint executor answers for");
    }

    [Fact]
    public void TheRangeSweepDiscriminator_IsTheSameOnBothSides()
    {
        Constant("discovery/executor.py", "SWEEP_NAME").Should().Be(
            ApiConstant("Discovery/RangeSweepParameters.cs", "WalkName"),
            "a Discover job naming this walk is what the sweep executor answers for");
    }

    [Fact]
    public void TheIcmpProbeDiscriminator_IsTheSameOnBothSides()
    {
        Constant("icmp/executor.py", "PROBE_NAME").Should().Be(
            ApiConstant("Reachability/IcmpProbeParameters.cs", "ProbeName"),
            "a Poll job naming this probe is what the reachability executor answers for");
    }

    [Fact]
    public void TheTwoDiscoverWalks_AreDifferentFromEachOther()
    {
        // The whole point of a discriminator. If these ever collided, each executor would answer
        // the other's jobs and the API's result handlers would read the wrong payloads.
        Constant("snmp/executor.py", "WALK_NAME")
            .Should().NotBe(Constant("discovery/executor.py", "SWEEP_NAME"));
    }

    [Fact]
    public void TheCollectorKnowsEveryJobKindTheApiCanQueue()
    {
        // Not a discriminator, but the layer above one: a kind the collector has no member for
        // would fail to parse a lease rather than be reported as unrunnable.
        string models = File.ReadAllText(Path.Combine(CollectorRoot, "models.py"));

        foreach (string kind in Enum.GetNames<CollectorJobKind>())
        {
            models.Should().Contain(
                $"\"{kind}\"",
                $"collector/models.py declares JobKind and the API can queue a {kind} job");
        }
    }

    /// <summary>
    /// The value of a module-level string constant in the collector, read as text on disk.
    /// </summary>
    /// <remarks>
    /// Text rather than reflection, the way every other rule that reads the collector works:
    /// there is no .NET handle on a Python value, and a rule that imported one would stop working
    /// the moment the collector's dependencies were not installed.
    /// </remarks>
    private static string Constant(string relativePath, string name)
    {
        Match declaration = PythonConstant(name)
            .Match(File.ReadAllText(Path.Combine(CollectorRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));

        declaration.Success.Should().BeTrue($"{relativePath} declares {name}");

        return declaration.Groups["value"].Value;
    }

    /// <summary>The value of a <c>const string</c> in the Inventory module, read the same way.</summary>
    private static string ApiConstant(string relativePath, string name)
    {
        string path = Path.Combine(
            Repository.Root,
            "src",
            "Modules",
            "NetShield.Inventory",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Match declaration = CSharpConstant(name).Match(File.ReadAllText(path));

        declaration.Success.Should().BeTrue($"{relativePath} declares {name}");

        return declaration.Groups["value"].Value;
    }

    private static Regex PythonConstant(string name) =>
        new("^" + Regex.Escape(name) + ":\\s*Final\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.Multiline);

    private static Regex CSharpConstant(string name) =>
        new(
            "const\\s+string\\s+" + Regex.Escape(name) + "\\s*=\\s*\"(?<value>[^\"]*)\"",
            RegexOptions.Multiline);
}
