using System.Text.RegularExpressions;

using FluentAssertions;

using NetShield.Contracts.Inventory;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The second list NetShield writes down twice: which neighbour protocols a vendor speaks.
/// </summary>
/// <remarks>
/// <para>
/// <c>NeighborSource</c> is a C# enum and every vendor adapter in <c>netshield-collector</c>
/// declares a <c>neighbor_protocols</c> set of the member names it is worth asking that platform
/// for. There is no generator between them, exactly as there is none for the vendor list, and
/// they are matched at runtime by string comparison when a walk result arrives.
/// </para>
/// <para>
/// The failure mode without this test is quiet in a way that matters. A protocol named on the
/// collector alone is read from devices and then arrives at an API with no member for it, so
/// every edge it found is dropped where the result is parsed — a whole protocol's worth of
/// topology silently missing, with no error anywhere. <see cref="VendorParityTests"/> exists for
/// the same reason and this is the same answer.
/// </para>
/// </remarks>
public sealed partial class NeighborProtocolParityTests
{
    private static readonly string AdapterRoot =
        Path.Combine(Repository.Root, "src", "netshield-collector", "collector", "vendors");

    [Fact]
    public void EveryProtocolTheCollectorDeclares_HasANeighborSourceMember()
    {
        IReadOnlyList<string> declared = DeclaredProtocols();

        declared.Should().NotBeEmpty("WP-2.1 gave every adapter a neighbour-protocol declaration");

        declared.Should().BeSubsetOf(
            Enum.GetNames<NeighborSource>(),
            "a protocol the collector reads and the API has no member for is a whole protocol's "
            + "worth of edges dropped where the result is parsed, with no error anywhere");
    }

    [Fact]
    public void TheProtocolConstants_AreSpelledTheSameOnBothSides()
    {
        // base.py names them once and every adapter refers to those constants, so this is where
        // a rename on one side alone is caught.
        Constant("LLDP").Should().Be(nameof(NeighborSource.Lldp));
        Constant("CDP").Should().Be(nameof(NeighborSource.Cdp));
    }

    [Fact]
    public void EveryAdapter_SpeaksLldp()
    {
        // IEEE 802.1AB is the standard and every platform SPEC.md §4 names implements it,
        // including the generic fallback: a device NetShield cannot identify is still asked.
        foreach ((string module, IReadOnlyList<string> protocols) in ProtocolsByAdapter())
        {
            protocols.Should().Contain(
                "LLDP",
                $"{module} declares the standard neighbour protocol");
        }
    }

    [Fact]
    public void Cdp_IsDeclaredByTheCiscoAdaptersAlone()
    {
        // CDP lives in Cisco's private MIB. If this ever grew a third member, the neighbour walk
        // would start reading an enterprise subtree from a device that has none — and the
        // alternative, asking every device, is the guess CONVENTIONS.md §5's vendor seam exists
        // to prevent.
        IReadOnlyList<string> speakers =
        [
            .. from entry in ProtocolsByAdapter()
               where entry.Protocols.Contains("CDP")
               select entry.Module
        ];

        speakers.Should().BeEquivalentTo(["cisco_ios.py", "cisco_nxos.py"]);
    }

    /// <summary>Every protocol name any adapter declares, resolved through <c>base.py</c>.</summary>
    private static IReadOnlyList<string> DeclaredProtocols() =>
        [.. ProtocolsByAdapter()
            .SelectMany(entry => entry.Protocols)
            .Distinct()
            .Select(Constant)];

    /// <summary>
    /// The constants each adapter's <c>neighbor_protocols</c> names, by module.
    /// </summary>
    /// <remarks>
    /// Text rather than reflection, the way every rule that reads the collector works: there is
    /// no .NET handle on a Python value, and a rule that imported one would stop working the
    /// moment the collector's dependencies were not installed. Adapters that declare nothing
    /// inherit <c>SnmpVendorAdapter</c>'s default, which <c>base.py</c> supplies and which this
    /// reads from the base module itself.
    /// </remarks>
    private static IReadOnlyList<(string Module, IReadOnlyList<string> Protocols)> ProtocolsByAdapter()
    {
        IReadOnlyList<string> inherited = Names(File.ReadAllText(Path.Combine(AdapterRoot, "base.py")));

        return
        [
            .. from file in Directory.EnumerateFiles(AdapterRoot, "*.py")
               where Path.GetFileName(file) is not ("base.py" or "__init__.py")
               let declared = Names(File.ReadAllText(file))
               select (Path.GetFileName(file), declared.Count > 0 ? declared : inherited)
        ];
    }

    /// <summary>The constant names inside the last <c>neighbor_protocols</c> declaration in a module.</summary>
    private static IReadOnlyList<string> Names(string source)
    {
        Match declaration = ProtocolDeclaration().Matches(source).LastOrDefault() is { } last
            ? last
            : Match.Empty;

        if (!declaration.Success)
        {
            return [];
        }

        return
        [
            .. declaration.Groups["members"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];
    }

    /// <summary>The value of a protocol constant, as <c>collector/vendors/base.py</c> defines it.</summary>
    private static string Constant(string name)
    {
        Match declaration = new Regex(
                "^" + Regex.Escape(name) + ":\\s*str\\s*=\\s*\"(?<value>[A-Za-z]+)\"",
                RegexOptions.Multiline)
            .Match(File.ReadAllText(Path.Combine(AdapterRoot, "base.py")));

        declaration.Success.Should().BeTrue($"collector/vendors/base.py defines {name}");

        return declaration.Groups["value"].Value;
    }

    [GeneratedRegex(
        """neighbor_protocols:\s*ClassVar\[frozenset\[str\]\]\s*=\s*frozenset\(\{(?<members>[^}]*)\}\)""",
        RegexOptions.Multiline)]
    private static partial Regex ProtocolDeclaration();
}
