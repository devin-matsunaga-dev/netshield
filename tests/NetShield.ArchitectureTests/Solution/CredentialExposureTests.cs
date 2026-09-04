using System.Reflection;
using System.Text.Json;

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
    /// The decrypt path stays inside the module. WP-1.2 was told it is callable only from the
    /// collector-job endpoint and left WP-1.3 to decide how far to widen it; WP-1.3's answer was
    /// not to widen it at all — the endpoint that needs it lives in this module, so nothing
    /// outside <c>NetShield.Inventory</c> can name any of these types to ask for one.
    /// </summary>
    /// <remarks>
    /// The last four are WP-1.3's additions: the shape a leased job carries to the collector and
    /// the credential inside it. They are the only types in the system besides the ones above
    /// that hold a plaintext credential, and they are deliberately not in
    /// <c>NetShield.Contracts</c> for exactly that reason.
    /// </remarks>
    [Theory]
    [InlineData("ICredentialResolver")]
    [InlineData("CredentialResolver")]
    [InlineData("ResolvedCredential")]
    [InlineData("CredentialMaterialPayload")]
    [InlineData("CredentialMaterialProtector")]
    [InlineData("CredentialProfile")]
    [InlineData("CollectorJobCredential")]
    [InlineData("CollectorCredentialMaterial")]
    [InlineData("CollectorJobLease")]
    [InlineData("CollectorJobBatch")]
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
            "ARCHITECTURE.md §7 puts the decrypt path behind the collector-job contract, and the "
            + "endpoint layer reaches it through a handler rather than naming it");
    }

    /// <summary>
    /// The allowlist. Exactly one handler opens a credential, and adding a second is a change
    /// somebody has to make here, in this list, where a reviewer will see it.
    /// </summary>
    /// <remarks>
    /// WP-1.2's rule was "no endpoint file mentions the resolver", which stopped a route being
    /// written by accident but said nothing about the rest of the system. Now that the path has a
    /// production caller the stronger statement can be made: these four files, and nothing else.
    /// </remarks>
    [Fact]
    public void OnlyTheLeaseHandler_OpensACredential()
    {
        string[] permitted =
        [
            "ICredentialResolver.cs",
            "CredentialResolver.cs",
            "CredentialKeyRewrapper.cs",
            "InventoryServiceCollectionExtensions.cs",
            "LeaseCollectorJobsHandler.cs"
        ];

        IReadOnlyList<string> offenders =
        [
            .. from file in Directory.EnumerateFiles(
                   Path.Combine(Repository.Root, "src"),
                   "*.cs",
                   SearchOption.AllDirectories)
               where !IsBuildOutput(file)
               where File.ReadAllText(file).Contains("ICredentialResolver", StringComparison.Ordinal)
               let name = Path.GetFileName(file)
               where !permitted.Contains(name, StringComparer.Ordinal)
               select Repository.RelativeToRoot(file)
        ];

        offenders.Should().BeEmpty(
            "a stored credential becomes plaintext in one place, and a second one is a decision "
            + "rather than an import");
    }

    /// <summary>
    /// The internal collector contract is not in the API the SPA is generated from. It carries an
    /// opened credential, and the committed document is what the TypeScript client is built out
    /// of — a path under <c>/internal</c> appearing there would put that shape in a browser's
    /// contract.
    /// </summary>
    [Fact]
    public void TheCommittedApiDocument_DescribesNoInternalPath()
    {
        string document = File.ReadAllText(
            Path.Combine(Repository.Root, "src/NetShield.Web.Host/openapi/v1.json"));

        using JsonDocument parsed = JsonDocument.Parse(document);

        IReadOnlyList<string> internalPaths =
        [
            .. from path in parsed.RootElement.GetProperty("paths").EnumerateObject()
               where path.Name.StartsWith("/internal", StringComparison.Ordinal)
               select path.Name
        ];

        internalPaths.Should().BeEmpty(
            "ARCHITECTURE.md §7's collector contract is not the API the SPA talks to");
    }

    private static bool IsBuildOutput(string path) =>
        Path.GetRelativePath(Repository.Root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

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
