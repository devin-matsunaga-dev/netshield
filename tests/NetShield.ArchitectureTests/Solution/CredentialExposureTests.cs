using System.Reflection;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The structural half of "a credential is write-only over the API": the type that holds
/// plaintext cannot be named outside its module, and the path that produces one is not reachable
/// from anything that serves HTTP.
/// </summary>
/// <remarks>
/// <c>ApiSecretExposureTests</c> checks the shapes the API returns and
/// <c>CredentialSecrecyTests</c> checks the bytes on the wire. This checks the thing neither can:
/// that there is no way to write such an endpoint by accident, because the types are not
/// available to whoever would write it.
/// </remarks>
public sealed class CredentialExposureTests
{
    private static readonly Assembly Inventory = typeof(NetShield.Inventory.Persistence.InventoryDbContext).Assembly;

    /// <summary>
    /// The decrypt path stays inside the module. WP-1.2 is told it is callable only from the
    /// collector-job endpoint, and that endpoint is WP-1.3's to design (ARCHITECTURE.md §7) —
    /// so nothing outside <c>NetShield.Inventory</c> can name the interface to ask for one, and
    /// widening it will be a deliberate line in that package's diff.
    /// </summary>
    [Theory]
    [InlineData("ICredentialResolver")]
    [InlineData("CredentialResolver")]
    [InlineData("ResolvedCredential")]
    [InlineData("CredentialMaterialPayload")]
    [InlineData("CredentialMaterialProtector")]
    [InlineData("CredentialProfile")]
    public void TheDecryptPath_IsNotPublic(string typeName)
    {
        Type type = Inventory.GetTypes().Single(candidate => candidate.Name == typeName);

        type.IsPublic.Should().BeFalse(
            "{0} is part of the path a stored credential becomes plaintext on, and nothing outside "
            + "NetShield.Inventory may name it",
            typeName);
    }

    /// <summary>
    /// No endpoint file reaches the decrypt path. The API's own routes have no business opening a
    /// credential, and the check is by name over the source because "nobody would" is not a rule.
    /// </summary>
    [Fact]
    public void NoEndpointFile_ReachesTheDecryptPath()
    {
        string[] forbidden = ["ICredentialResolver", "CredentialMaterialProtector.Open", "ResolvedCredential"];

        IReadOnlyList<string> offenders =
        [
            .. from file in Directory.EnumerateFiles(
                   Path.Combine(Repository.Root, "src"),
                   "*Endpoints.cs",
                   SearchOption.AllDirectories)
               let source = File.ReadAllText(file)
               from name in forbidden
               where source.Contains(name, StringComparison.Ordinal)
               select $"{Path.GetFileName(file)} mentions {name}"
        ];

        offenders.Should().BeEmpty(
            "ARCHITECTURE.md §7 puts the decrypt path behind the collector-job contract, and "
            + "WP-1.2 gives it no HTTP surface at all");
    }

    /// <summary>
    /// Key rotation is not an endpoint either. It is the most privileged cryptographic operation
    /// in the system and it runs as <c>NetShield.Web.Host --rewrap</c>; a route would put it
    /// permanently on the web attack surface to gain an audit row the command writes itself.
    /// </summary>
    [Fact]
    public void NoEndpointFile_ExposesKeyRotation()
    {
        IReadOnlyList<string> offenders =
        [
            .. from file in Directory.EnumerateFiles(
                   Path.Combine(Repository.Root, "src"),
                   "*Endpoints.cs",
                   SearchOption.AllDirectories)
               where File.ReadAllText(file).Contains("CredentialKeyRewrapper", StringComparison.Ordinal)
               select Path.GetFileName(file)
        ];

        offenders.Should().BeEmpty("key rotation is a command, not a route");
    }
}
