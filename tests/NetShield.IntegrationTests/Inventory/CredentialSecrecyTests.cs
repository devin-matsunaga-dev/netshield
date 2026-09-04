using System.Text;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Persistence;
using NetShield.Platform.Results;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The four promises WP-1.2 makes about a stored credential, each checked against the thing that
/// would actually break it: the row on disk, every response body, the audit table, and the one
/// path that is allowed to read the plaintext back.
/// </summary>
public sealed class CredentialSecrecyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Profiles = "/api/v1/credential-profiles";

    /// <summary>Distinctive enough that finding it anywhere is unambiguous.</summary>
    private const string Community = "zzq-community-that-must-never-appear";

    private const string PrivateKey =
        "-----BEGIN OPENSSH PRIVATE KEY-----\nzzq-key-that-must-never-appear\n-----END OPENSSH PRIVATE KEY-----\n";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// "A stored secret is unreadable in the database without the KEK", read literally: the bytes
    /// in the two columns contain nothing of the secret, in any encoding a grep would find.
    /// </summary>
    [Fact]
    public async Task TheStoredRow_ContainsNothingOfTheSecret()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateCommunityAsync(host);

        StoredCiphertext stored = await host.CiphertextAsync(id, Cancellation);

        Encoding.UTF8.GetString(stored.MaterialCiphertext).Should().NotContain(Community);
        Convert.ToBase64String(stored.MaterialCiphertext).Should()
            .NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Community)));

        stored.WrappedDataKey.Should().NotBeEmpty();
        stored.KeyId.Should().Be(InventoryHost.ActiveKeyId);
    }

    /// <summary>
    /// The same row, read by something holding a different key ring — which is what an attacker
    /// with a copy of the database has. It does not open.
    /// </summary>
    [Fact]
    public async Task TheStoredRow_DoesNotOpenUnderADifferentKeyRing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateCommunityAsync(host);

        await using InventoryHost withAnotherKey = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            database: host.ConnectionString,
            keyRing: [(InventoryHost.ActiveKeyId, InventoryHost.RotatedKeyEncryptionKey)]);

        await withAnotherKey.Invoking(subject => subject.InScopeAsync(services =>
                services.GetRequiredService<ICredentialResolver>().ResolveAsync(id, Cancellation)))
            .Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
    }

    /// <summary>
    /// Every response the API can produce about a profile, checked for the actual secret rather
    /// than for a member name. <c>ApiSecretExposureTests</c> checks the shapes; this checks the
    /// bytes that came off the wire.
    /// </summary>
    [Fact]
    public async Task NoResponseBody_CarriesTheMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Edge SSH",
                CredentialKind.SshKey,
                new CredentialMaterial(PrivateKey: PrivateKey, PrivateKeyPassword: Community),
                Username: "netshield"),
            Cancellation);

        created.Status.Should().Be(201);

        Guid id = CredentialProfileCrudTests.Read(created).Id;

        ApiResponse[] responses =
        [
            created,
            await host.Client.GetAsync($"{Profiles}/{id}", Cancellation),
            await host.Client.GetAsync(Profiles, Cancellation),
            await host.Client.PutAsync(
                $"{Profiles}/{id}",
                new UpdateCredentialProfileRequest("Edge SSH", Username: "netshield"),
                Cancellation),
            await host.Client.PutAsync(
                $"{Profiles}/{id}/material",
                new ReplaceCredentialMaterialRequest(new CredentialMaterial(PrivateKey: PrivateKey)),
                Cancellation)
        ];

        responses.Should().AllSatisfy(response =>
        {
            response.Body.Should().NotContain(Community);
            response.Body.Should().NotContain("zzq-key-that-must-never-appear");
        });
    }

    /// <summary>
    /// <c>audit_log</c> is the one table a leak can never be taken back out of, because
    /// ARCHITECTURE.md §8 admits no update and no delete path for it.
    /// </summary>
    [Fact]
    public async Task NoAuditRow_CarriesTheMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateCommunityAsync(host);

        await host.Client.PutAsync(
            $"{Profiles}/{id}/material",
            new ReplaceCredentialMaterialRequest(new CredentialMaterial(Community: Community)),
            Cancellation);

        // Read as two columns and joined here. They are jsonb, and concatenating them in the
        // query would ask PostgreSQL to cast a snapshot to text mid-statement.
        IReadOnlyList<(string? Before, string? After)> rows = await host.InScopeAsync(async services =>
            (IReadOnlyList<(string?, string?)>)await services.GetRequiredService<PlatformDbContext>()
                .Set<AuditEntry>().AsNoTracking()
                .Where(entry => entry.Before != null || entry.After != null)
                .Select(entry => new ValueTuple<string?, string?>(entry.Before, entry.After))
                .ToListAsync(Cancellation));

        IReadOnlyList<string> payloads =
            [.. rows.Select(row => (row.Before ?? string.Empty) + (row.After ?? string.Empty))];

        payloads.Should().NotBeEmpty("a rotation and a create both record a snapshot");
        payloads.Should().AllSatisfy(payload => payload.Should().NotContain(Community));
    }

    /// <summary>
    /// The rotation audit row says when the material changed and nothing about what it changed
    /// to. Both snapshots carry <c>materialUpdatedAt</c>, and the two differ.
    /// </summary>
    [Fact]
    public async Task TheRotationAuditRow_RecordsThatTheMaterialChangedAndNotWhatItIs()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateCommunityAsync(host);

        await host.Client.PutAsync(
            $"{Profiles}/{id}/material",
            new ReplaceCredentialMaterialRequest(new CredentialMaterial(Community: "rotated")),
            Cancellation);

        (string? Before, string? After) row = await host.InScopeAsync(async services =>
            await services.GetRequiredService<PlatformDbContext>()
                .Set<AuditEntry>().AsNoTracking()
                .Where(entry => entry.Action == "inventory.credential-profile-rotate")
                .Select(entry => new ValueTuple<string?, string?>(entry.Before, entry.After))
                .SingleAsync(Cancellation));

        row.Before.Should().Contain("materialUpdatedAt");
        row.After.Should().Contain("materialUpdatedAt");
        row.Before.Should().NotBe(row.After);
    }

    /// <summary>
    /// The decrypt path, which is the only thing anywhere that reads a credential back. It is
    /// internal to the Inventory module and has no HTTP surface at all in this package — this
    /// test resolves it out of the container by hand, which is what WP-1.3's collector-job
    /// endpoint will do from inside the module.
    /// </summary>
    [Fact]
    public async Task TheDecryptPath_ReturnsExactlyWhatWasStored()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Edge SSH",
                CredentialKind.SshKey,
                new CredentialMaterial(PrivateKey: PrivateKey, PrivateKeyPassword: "protected"),
                Username: "netshield"),
            Cancellation);

        Guid id = CredentialProfileCrudTests.Read(created).Id;

        Result<ResolvedCredential> resolved = await host.InScopeAsync(services =>
            services.GetRequiredService<ICredentialResolver>().ResolveAsync(id, Cancellation));

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Kind.Should().Be(CredentialKind.SshKey);
        resolved.Value.Username.Should().Be("netshield");

        // Byte for byte, including the trailing newline: PEM is line-oriented and a key that was
        // trimmed on the way in is a key that no longer parses on the way out.
        resolved.Value.Material.PrivateKey.Should().Be(PrivateKey);
        resolved.Value.Material.PrivateKeyPassword.Should().Be("protected");
    }

    [Fact]
    public async Task TheDecryptPath_RefusesAProfileThatHasBeenRemoved()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateCommunityAsync(host);

        await host.Client.DeleteAsync($"{Profiles}/{id}", Cancellation);

        Result<ResolvedCredential> resolved = await host.InScopeAsync(services =>
            services.GetRequiredService<ICredentialResolver>().ResolveAsync(id, Cancellation));

        resolved.IsSuccess.Should().BeFalse();
        resolved.Error!.Kind.Should().Be(ErrorKind.NotFound);
    }

    /// <summary>
    /// A profile's material never reaches a log line, at any level — SPEC.md §5, and the reason
    /// <c>SecretRedactor</c> is applied at the sink rather than at each call site.
    /// </summary>
    [Fact]
    public async Task NoLogLine_CarriesTheMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateCommunityAsync(host);

        await host.InScopeAsync(services =>
            services.GetRequiredService<ICredentialResolver>().ResolveAsync(id, Cancellation));

        host.RecordedLogs().Should().AllSatisfy(line => line.Should().NotContain(Community));
    }

    private static async Task<Guid> CreateCommunityAsync(InventoryHost host)
    {
        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Core community",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: Community)),
            TestContext.Current.CancellationToken);

        created.Status.Should().Be(201);

        return CredentialProfileCrudTests.Read(created).Id;
    }
}
