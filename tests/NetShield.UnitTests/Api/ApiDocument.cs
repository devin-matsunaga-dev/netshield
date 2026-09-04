using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.OpenApi;

using NetShield.Identity;
using NetShield.Identity.Endpoints;

using NetShield.Inventory;
using NetShield.Inventory.Endpoints;

using NetShield.Platform;
using NetShield.Platform.Api;
using NetShield.Platform.Auditing;
using NetShield.Platform.Problems;

namespace NetShield.UnitTests.Api;

/// <summary>
/// Produces the OpenAPI document the API describes itself with, so a test can hold it against
/// the copy committed for the TypeScript client.
/// </summary>
/// <remarks>
/// <para>
/// It maps what <c>NetShield.Web.Host/Program.cs</c> maps, and <c>ApiDocumentParityTests</c>
/// fails if the two ever stop agreeing — a module whose endpoints the composition root maps and
/// this does not would otherwise go missing from the document, and from the client, silently.
/// </para>
/// <para>
/// It cannot simply boot the composition root: ARCHITECTURE.md §4 lets nothing reference
/// <c>NetShield.Web.Host</c>, and the host's Aspire connection strings do not exist here anyway.
/// </para>
/// </remarks>
internal static class ApiDocument
{
    /// <summary>The committed copy, relative to the repository root.</summary>
    public const string CommittedPath = "src/NetShield.Web.Host/openapi/v1.json";

    /// <summary>The committed copy's absolute path.</summary>
    public static string CommittedFile { get; } = Path.Combine(RepositoryRoot(), CommittedPath);

    /// <summary>Set this to rewrite <see cref="CommittedPath"/> instead of asserting on it.</summary>
    public const string UpdateVariable = "NETSHIELD_UPDATE_OPENAPI";

    /// <summary>A fixture key-encryption key: base64 of the bytes 0x00 to 0x1f, in order.</summary>
    public const string TestKeyEncryptionKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>
    /// A fixture collector shared secret. The module validates one at startup, and this host has
    /// to start for a routing table to exist. It authenticates nothing — no request is ever made
    /// over this host's socket.
    /// </summary>
    public const string TestCollectorSecret = "document-fixture-collector-secret-0000000000";

    /// <summary>The document as JSON, exactly as it is committed.</summary>
    public static async Task<string> GenerateAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            // Not Development: the container would validate every registration on build, and the
            // stores those registrations reach are not this test's business.
            EnvironmentName = "Production",
            ApplicationName = typeof(ApiDocument).Assembly.GetName().Name
        });

        // A free loopback port. The host has to start for the routing table to exist — the
        // document is built from the endpoint data sources, and those are registered as the
        // pipeline is built, not as each route is mapped. Nothing is ever requested over it.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // The Inventory module validates its key ring and its collector secret at startup, so the
        // host needs both to start. The key bytes are 0x00..0x1f in order — recognisably not a key
        // anybody generated, sealing nothing, and reading no row. CONVENTIONS.md §9 forbids
        // committing a secret; these are fixtures that let a document be produced.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:CredentialEncryption:ActiveKeyId"] = "test",
            ["Security:CredentialEncryption:Keys:test"] = TestKeyEncryptionKey,
            ["Collector:SharedSecret"] = TestCollectorSecret
        });

        builder.AddNetShieldPlatform();
        builder.Services.AddNetShieldProblemDetails();
        builder.AddNetShieldAuthorization();
        builder.AddNetShieldAudit();
        builder.AddNetShieldIdentity();
        builder.AddNetShieldInventory();
        builder.AddNetShieldApiDocument();

        WebApplication application = builder.Build();

        // Kept in step with NetShield.Web.Host/Program.cs — see ApiDocumentParityTests.
        application.MapIdentityEndpoints();
        application.MapInventoryEndpoints();

        await application.StartAsync(cancellationToken);

        try
        {
            // Keyed by document name: AddOpenApi registers one provider per document.
            OpenApiDocument document = await application.Services
                .GetRequiredKeyedService<IOpenApiDocumentProvider>(NetShieldApiDocument.Name)
                .GetOpenApiDocumentAsync(cancellationToken);

            return Normalize(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken));
        }
        finally
        {
            await application.StopAsync(cancellationToken);
            await application.DisposeAsync();
        }
    }

    /// <summary>The directory holding <c>NetShield.sln</c>, found by walking up from the assembly.</summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NetShield.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate NetShield.sln above the test assembly.");
    }

    /// <summary>
    /// One trailing newline and no carriage returns, so the file reads the same on every
    /// platform and a diff on it is about the API rather than about line endings.
    /// </summary>
    public static string Normalize(string document) =>
        document.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
}
