using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory.Credentials;

using NetShield.Platform.Auditing;
using NetShield.Platform.Persistence;
using NetShield.Platform.Results;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Key rotation: <c>NetShield.Web.Host --rewrap</c>, exercised through the type the command runs.
/// </summary>
/// <remarks>
/// A rotation is two hosts over one set of rows — one holding the old key, one holding both — so
/// every test here starts a second host against the first one's database. That is what a real
/// rotation looks like, and it is the only way to show that the re-wrap is what makes the old key
/// retirable.
/// </remarks>
public sealed class CredentialRotationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Profiles = "/api/v1/credential-profiles";

    private const string Community = "zzq-community-that-must-survive-rotation";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole of "rotation re-wraps without downtime": the payload column is not rewritten,
    /// only the wrapped key is, and the plaintext still comes back afterwards.
    /// </summary>
    [Fact]
    public async Task Rewrap_MovesEveryProfileToTheActiveKey_WithoutTouchingThePayload()
    {
        await using InventoryHost original = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(original);

        StoredCiphertext before = await original.CiphertextAsync(id, Cancellation);

        await using InventoryHost rotating = await RotatingHost(original);

        CredentialRewrapReport report = await rotating.InScopeAsync(services =>
            services.GetRequiredService<CredentialKeyRewrapper>().RewrapAsync(Cancellation));

        report.ActiveKeyId.Should().Be("rotated");
        report.Examined.Should().Be(1);
        report.Rewrapped.Should().Be(1);

        StoredCiphertext after = await rotating.CiphertextAsync(id, Cancellation);

        after.KeyId.Should().Be("rotated");
        after.MaterialCiphertext.Should().Equal(before.MaterialCiphertext);
        after.WrappedDataKey.Should().NotEqual(before.WrappedDataKey);
    }

    /// <summary>
    /// What the rotation is for: once it has run, the previous key can be dropped from the ring
    /// and everything still opens.
    /// </summary>
    [Fact]
    public async Task AfterRewrap_TheOldKeyCanBeRetiredAndTheMaterialStillOpens()
    {
        await using InventoryHost original = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(original);

        await using (InventoryHost rotating = await RotatingHost(original))
        {
            await rotating.InScopeAsync(services =>
                services.GetRequiredService<CredentialKeyRewrapper>().RewrapAsync(Cancellation));
        }

        await using InventoryHost retired = await InventoryHost.StartAsync(
            postgres,
            Cancellation,
            database: original.ConnectionString,
            keyRing: [("rotated", InventoryHost.RotatedKeyEncryptionKey)],
            activeKeyId: "rotated");

        Result<ResolvedCredential> resolved = await retired.InScopeAsync(services =>
            services.GetRequiredService<ICredentialResolver>().ResolveAsync(id, Cancellation));

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Material.Community.Should().Be(Community);
    }

    /// <summary>
    /// Safe to repeat, which is how an operator knows the key they are retiring is free: a second
    /// run finds nothing left to examine.
    /// </summary>
    [Fact]
    public async Task Rewrap_RunTwice_MovesNothingTheSecondTime()
    {
        await using InventoryHost original = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(original);

        await using InventoryHost rotating = await RotatingHost(original);

        await rotating.InScopeAsync(services =>
            services.GetRequiredService<CredentialKeyRewrapper>().RewrapAsync(Cancellation));

        CredentialRewrapReport second = await rotating.InScopeAsync(services =>
            services.GetRequiredService<CredentialKeyRewrapper>().RewrapAsync(Cancellation));

        second.Examined.Should().Be(0);
        second.Rewrapped.Should().Be(0);
    }

    /// <summary>
    /// A soft-deleted profile is still a row, and a key can only be deleted once nothing depends
    /// on it. Skipping removed rows would leave a key that could never be retired.
    /// </summary>
    [Fact]
    public async Task Rewrap_MovesRemovedProfilesToo_SoTheOldKeyCanActuallyBeDropped()
    {
        await using InventoryHost original = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(original);

        await original.Client.DeleteAsync($"{Profiles}/{id}", Cancellation);

        await using InventoryHost rotating = await RotatingHost(original);

        CredentialRewrapReport report = await rotating.InScopeAsync(services =>
            services.GetRequiredService<CredentialKeyRewrapper>().RewrapAsync(Cancellation));

        report.Rewrapped.Should().Be(1);

        (await rotating.CiphertextAsync(id, Cancellation)).KeyId.Should().Be("rotated");
    }

    /// <summary>
    /// A rotation is the most privileged cryptographic operation in the system and it is not on
    /// the web surface, so nothing writes its audit row for it. It writes its own.
    /// </summary>
    [Fact]
    public async Task Rewrap_WritesItsOwnAuditRow_NamingTheKeyIdAndNoKeyMaterial()
    {
        await using InventoryHost original = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(original);

        await using InventoryHost rotating = await RotatingHost(original);

        await rotating.InScopeAsync(services =>
            services.GetRequiredService<CredentialKeyRewrapper>().RewrapAsync(Cancellation));

        AuditEntry row = await rotating.InScopeAsync(async services =>
            await services.GetRequiredService<PlatformDbContext>()
                .Set<AuditEntry>().AsNoTracking()
                .SingleAsync(entry => entry.Action == CredentialKeyRewrapper.AuditAction, Cancellation));

        row.Outcome.Should().Be(AuditOutcome.Succeeded);
        row.TargetType.Should().Be("credential-profile");
        row.HttpMethod.Should().Be("COMMAND");
        row.Path.Should().Contain("--rewrap");

        // A key id names which key is active; it opens nothing. The key itself is nowhere.
        // Matched loosely: the column is jsonb and PostgreSQL renders it back with its own
        // spacing, so asserting on the exact bytes would be asserting on the server's formatter.
        row.After.Should().Contain("activeKeyId").And.Contain("rotated");
        row.After.Should().Contain("rewrapped");
        row.After.Should().NotContain(InventoryHost.RotatedKeyEncryptionKey);
        row.After.Should().NotContain(InventoryHost.KeyEncryptionKey);
    }

    /// <summary>
    /// Rotating the material also rotates the wrapping, because new material is always sealed
    /// under whichever key is active now.
    /// </summary>
    [Fact]
    public async Task ReplacingTheMaterial_SealsItUnderTheActiveKey()
    {
        await using InventoryHost original = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(original);

        await using InventoryHost rotating = await RotatingHost(original);

        await rotating.Client.PutAsync(
            $"{Profiles}/{id}/material",
            new ReplaceCredentialMaterialRequest(new CredentialMaterial(Community: "replaced")),
            Cancellation);

        (await rotating.CiphertextAsync(id, Cancellation)).KeyId.Should().Be("rotated");
    }

    /// <summary>A host started on a ring it cannot use refuses to start, rather than half working.</summary>
    [Fact]
    public async Task AHostWithAMalformedKeyRing_DoesNotStart()
    {
        await FluentActions.Invoking(() => InventoryHost.StartAsync(
                postgres,
                Cancellation,
                keyRing: [("test", "this-is-not-a-key")]))
            .Should().ThrowAsync<Exception>();
    }

    /// <summary>A second host over the first one's rows, holding both keys, active on the new one.</summary>
    private Task<InventoryHost> RotatingHost(InventoryHost original) =>
        InventoryHost.StartAsync(
            postgres,
            Cancellation,
            database: original.ConnectionString,
            keyRing:
            [
                (InventoryHost.ActiveKeyId, InventoryHost.KeyEncryptionKey),
                ("rotated", InventoryHost.RotatedKeyEncryptionKey)
            ],
            activeKeyId: "rotated");

    private static async Task<Guid> CreateAsync(InventoryHost host)
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
