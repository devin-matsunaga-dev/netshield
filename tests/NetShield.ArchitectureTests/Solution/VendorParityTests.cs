using System.Text.RegularExpressions;

using FluentAssertions;

using NetShield.Contracts.Inventory;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The one list in NetShield that is written down twice: the vendors SPEC.md §4 names.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeviceVendor</c> is a C# enum and each vendor adapter in <c>netshield-collector</c>
/// declares the member's name as a string. There is no generator between them — the collector
/// contract is deliberately absent from the OpenAPI document (WP-1.3), so nothing derives one
/// list from the other — and they are matched at runtime by string comparison when a walk result
/// arrives.
/// </para>
/// <para>
/// The failure mode without this test is quiet. A vendor added to the collector alone resolves
/// on that side and then arrives at an API that has no member for it; a vendor added to the API
/// alone is a member nothing can ever produce. The first is handled at runtime — the walk
/// handler leaves the device's vendor alone and records the mismatch rather than pretending the
/// device was unrecognised — but handling it is a safety net, not a substitute for the two lists
/// agreeing.
/// </para>
/// </remarks>
public sealed partial class VendorParityTests
{
    private static readonly string AdapterRoot =
        Path.Combine(Repository.Root, "src", "netshield-collector", "collector", "vendors");

    [Fact]
    public void EveryCollectorAdapter_NamesADeviceVendorMemberThatExists()
    {
        IReadOnlyList<string> declared = AdapterVendors();

        declared.Should().NotBeEmpty("WP-1.5 added the seven adapters SPEC.md §4 names");

        declared.Should().BeSubsetOf(
            Enum.GetNames<DeviceVendor>(),
            "an adapter naming a vendor the API has no member for resolves on the collector and "
            + "then cannot be applied");
    }

    [Fact]
    public void EveryDeviceVendor_ExceptUnknown_HasACollectorAdapter()
    {
        // Unknown is the one member no adapter answers for: it means nothing has looked at the
        // device yet, which is a state of the inventory rather than a platform to be recognised.
        IReadOnlyList<string> expected =
        [
            .. Enum.GetNames<DeviceVendor>().Where(name => name != nameof(DeviceVendor.Unknown))
        ];

        AdapterVendors().Should().BeEquivalentTo(
            expected,
            "SPEC.md §4 fixes one list, and a member with no adapter is a vendor nothing can "
            + "ever resolve to");
    }

    /// <summary>
    /// The vendor each adapter module declares, read as text on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Text rather than reflection, the way every other rule in this project reads the collector:
    /// there is no .NET handle on a Python class, and a rule that had to import one would stop
    /// working the moment the collector's dependencies were not installed.
    /// </para>
    /// <para>
    /// The generic adapter names its vendor through the <c>GENERIC_SNMP</c> constant rather than
    /// as a literal, because the registry's fallback has to be able to name it too. The constant
    /// is resolved from <c>base.py</c> rather than assumed here, so this test reads the
    /// collector's own value for it and would fail if that were changed on one side alone —
    /// which is the whole point of the test.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> AdapterVendors()
    {
        string fallback = FallbackVendor();

        return
        [
            .. from file in Directory.EnumerateFiles(AdapterRoot, "*.py")
               where Path.GetFileName(file) != "base.py" && Path.GetFileName(file) != "__init__.py"
               let declaration = VendorDeclaration().Match(File.ReadAllText(file))
               where declaration.Success
               select declaration.Groups["vendor"].Success
                   ? declaration.Groups["vendor"].Value
                   : fallback
        ];
    }

    /// <summary>The value of <c>GENERIC_SNMP</c>, as <c>collector/vendors/base.py</c> defines it.</summary>
    private static string FallbackVendor()
    {
        Match declaration = FallbackDeclaration()
            .Match(File.ReadAllText(Path.Combine(AdapterRoot, "base.py")));

        declaration.Success.Should().BeTrue(
            "collector/vendors/base.py defines the fallback vendor's name and this test reads it");

        return declaration.Groups["vendor"].Value;
    }

    [GeneratedRegex(
        """^\s*vendor:\s*ClassVar\[str\]\s*=\s*(?:GENERIC_SNMP|"(?<vendor>[A-Za-z]+)")""",
        RegexOptions.Multiline)]
    private static partial Regex VendorDeclaration();

    [GeneratedRegex(
        """^GENERIC_SNMP:\s*str\s*=\s*"(?<vendor>[A-Za-z]+)""",
        RegexOptions.Multiline)]
    private static partial Regex FallbackDeclaration();
}
