using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform.Auditing;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Credential profile CRUD against real PostgreSQL: the happy path, the refusals, and the two
/// things that only hold because the database enforces them — the case-insensitive unique name
/// among live rows, and the soft delete that releases it.
/// </summary>
public sealed class CredentialProfileCrudTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    internal const string Profiles = "/api/v1/credential-profiles";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_AValidProfile_Returns201WithLocationAndNoMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            SnmpV3Request("Core SNMP"),
            Cancellation);

        created.Status.Should().Be(201);

        CredentialProfileDetail profile = Read(created);

        profile.Name.Should().Be("Core SNMP");
        profile.Kind.Should().Be(CredentialKind.SnmpV3);
        profile.Username.Should().Be("netshield");
        profile.AuthAlgorithm.Should().Be(SnmpAuthAlgorithm.Sha256);
        profile.PrivacyAlgorithm.Should().Be(SnmpPrivacyAlgorithm.Aes128);
        profile.DeviceCount.Should().Be(0);

        created.Json.TryGetProperty("material", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_AProfile_ReturnsItWithoutMaterial()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        ApiResponse fetched = await host.Client.GetAsync($"{Profiles}/{id}", Cancellation);

        fetched.Status.Should().Be(200);
        Read(fetched).Id.Should().Be(id);
        fetched.Body.Should().NotContain("authentication-pass-phrase");
    }

    [Fact]
    public async Task Get_AProfileThatDoesNotExist_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse missing = await host.Client.GetAsync($"{Profiles}/{Guid.CreateVersion7()}", Cancellation);

        missing.Status.Should().Be(404);
        missing.Member("code").Should().Be("credential-profile.not-found");
    }

    /// <summary>
    /// Uniqueness is decided on the folded name: two profiles a person would read as the same
    /// name are the same name, whichever way they were typed.
    /// </summary>
    [Fact]
    public async Task Create_ANameALiveProfileHolds_Returns409_EvenInADifferentCase()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "Core SNMP", Cancellation);

        ApiResponse conflict = await host.Client.PostAsync(
            Profiles,
            SnmpV3Request("core snmp"),
            Cancellation);

        conflict.Status.Should().Be(409);
        conflict.Member("code").Should().Be("credential-profile.duplicate-name");
    }

    /// <summary>
    /// Soft delete releases the name, the way a removed device releases its address. The row
    /// stays so the audit rows naming it still resolve.
    /// </summary>
    [Fact]
    public async Task Create_ANameARemovedProfileHeld_Succeeds()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        (await host.Client.DeleteAsync($"{Profiles}/{id}", Cancellation)).Status.Should().Be(204);

        (await host.Client.PostAsync(Profiles, SnmpV3Request("Core SNMP"), Cancellation))
            .Status.Should().Be(201);
    }

    [Fact]
    public async Task Delete_AProfileAlreadyDeleted_Returns404()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.DeleteAsync($"{Profiles}/{id}", Cancellation);

        (await host.Client.DeleteAsync($"{Profiles}/{id}", Cancellation)).Status.Should().Be(404);
    }

    [Fact]
    public async Task Update_ReplacesTheAttributesAndClearsAnOmittedOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation, "The read-only community");

        ApiResponse updated = await host.Client.PutAsync(
            $"{Profiles}/{id}",
            new UpdateCredentialProfileRequest(
                "Edge SNMP",
                Username: "netshield",
                AuthAlgorithm: SnmpAuthAlgorithm.Sha512,
                PrivacyAlgorithm: SnmpPrivacyAlgorithm.Aes128),
            Cancellation);

        updated.Status.Should().Be(200);

        CredentialProfileDetail profile = Read(updated);

        profile.Name.Should().Be("Edge SNMP");
        profile.AuthAlgorithm.Should().Be(SnmpAuthAlgorithm.Sha512);

        // A PUT describes the profile as it should now be, so an omitted member clears it.
        profile.Description.Should().BeNull();
    }

    /// <summary>
    /// The kind decides what the sealed blob holds, and there is no member on the update request
    /// to change it with. This asserts the contract rather than a handler branch.
    /// </summary>
    [Fact]
    public async Task Update_CannotChangeTheKind_BecauseTheRequestHasNoMemberForIt()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Profiles}/{id}",
            new UpdateCredentialProfileRequest(
                "Core SNMP",
                Username: "netshield",
                AuthAlgorithm: SnmpAuthAlgorithm.Sha256,
                PrivacyAlgorithm: SnmpPrivacyAlgorithm.Aes128),
            Cancellation);

        Read(await host.Client.GetAsync($"{Profiles}/{id}", Cancellation))
            .Kind.Should().Be(CredentialKind.SnmpV3);

        typeof(UpdateCredentialProfileRequest).GetProperty("Kind").Should().BeNull();
    }

    /// <summary>
    /// Switching privacy on or off changes which members the stored material must carry, and the
    /// material is not being replaced by an update. Refusing is the only answer that leaves the
    /// profile consistent.
    /// </summary>
    [Fact]
    public async Task Update_ThatSwitchesPrivacyOff_IsRefusedWith422()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        ApiResponse refused = await host.Client.PutAsync(
            $"{Profiles}/{id}",
            new UpdateCredentialProfileRequest(
                "Core SNMP",
                Username: "netshield",
                AuthAlgorithm: SnmpAuthAlgorithm.Sha256,
                PrivacyAlgorithm: SnmpPrivacyAlgorithm.None),
            Cancellation);

        refused.Status.Should().Be(422);
        refused.Member("code").Should().Be("credential-profile.attributes-invalid");
    }

    [Fact]
    public async Task Create_WithMaterialThatDoesNotSuitTheKind_Returns422()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Core SNMP",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Password: "wrong-member-for-this-kind")),
            Cancellation);

        refused.Status.Should().Be(422);
        refused.Member("code").Should().Be("credential-profile.material-incomplete");
    }

    [Fact]
    public async Task Create_WithAttributesThatDoNotSuitTheKind_Returns422()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "Core community",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: "read-only"),
                Username: "there-is-no-user-behind-a-community"),
            Cancellation);

        refused.Status.Should().Be(422);
        refused.Member("code").Should().Be("credential-profile.attributes-invalid");
    }

    [Fact]
    public async Task Create_WithNoName_Returns400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                "  ",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: "read-only")),
            Cancellation);

        refused.Status.Should().Be(400);
    }

    [Fact]
    public async Task List_IsFilteredByKindAndPagedByKeyset()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "SNMP one", Cancellation);
        await CreateAsync(host, "SNMP two", Cancellation);
        await CreateCommunityAsync(host, "Community one", Cancellation);

        ApiResponse all = await host.Client.GetAsync($"{Profiles}?limit=2", Cancellation);

        all.Status.Should().Be(200);
        all.Json.GetProperty("items").GetArrayLength().Should().Be(2);
        all.Json.GetProperty("totalCount").GetInt64().Should().Be(3);
        all.Member("nextCursor").Should().NotBeNullOrEmpty();

        ApiResponse next = await host.Client.GetAsync(
            $"{Profiles}?limit=2&cursor={Uri.EscapeDataString(all.Member("nextCursor")!)}",
            Cancellation);

        next.Json.GetProperty("items").GetArrayLength().Should().Be(1);

        ApiResponse communities = await host.Client.GetAsync($"{Profiles}?kind=SnmpV2c", Cancellation);

        communities.Json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task List_WithAnUnknownSortField_Returns400()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        ApiResponse refused = await host.Client.GetAsync($"{Profiles}?sort=nonsense", Cancellation);

        refused.Status.Should().Be(400);
        refused.Member("code").Should().Be("credential-profile.unknown-sort");
    }

    [Fact]
    public async Task List_SearchesTheNameCaseInsensitively()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        await CreateAsync(host, "Core SNMP", Cancellation);
        await CreateAsync(host, "Edge SNMP", Cancellation);

        ApiResponse found = await host.Client.GetAsync($"{Profiles}?search=cORe", Cancellation);

        found.Json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task EveryMutation_WritesOneOutboxRow_InTheSameTransaction()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Profiles}/{id}",
            new UpdateCredentialProfileRequest(
                "Core SNMP renamed",
                Username: "netshield",
                AuthAlgorithm: SnmpAuthAlgorithm.Sha256,
                PrivacyAlgorithm: SnmpPrivacyAlgorithm.Aes128),
            Cancellation);

        await host.Client.PutAsync(
            $"{Profiles}/{id}/material",
            new ReplaceCredentialMaterialRequest(
                new CredentialMaterial(AuthPassword: "rotated", PrivacyPassword: "rotated-too")),
            Cancellation);

        await host.Client.DeleteAsync($"{Profiles}/{id}", Cancellation);

        (await host.OutboxEventNamesAsync(Cancellation)).Should().Equal(
            typeof(CredentialProfileCreated).FullName,
            typeof(CredentialProfileUpdated).FullName,
            typeof(CredentialProfileUpdated).FullName,
            typeof(CredentialProfileRemoved).FullName);
    }

    [Fact]
    public async Task EveryMutation_WritesAnAuditRow()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid id = await CreateAsync(host, "Core SNMP", Cancellation);

        await host.Client.PutAsync(
            $"{Profiles}/{id}/material",
            new ReplaceCredentialMaterialRequest(
                new CredentialMaterial(AuthPassword: "rotated", PrivacyPassword: "rotated-too")),
            Cancellation);

        await host.Client.DeleteAsync($"{Profiles}/{id}", Cancellation);

        IReadOnlyList<AuditRow> rows = await host.AuditRowsAsync(Cancellation);

        rows.Where(row => row.TargetType == "credential-profile").Select(row => row.Action)
            .Should().Equal(
                "inventory.credential-profile-create",
                "inventory.credential-profile-rotate",
                "inventory.credential-profile-delete");

        rows.Where(row => row.TargetType == "credential-profile")
            .Should().AllSatisfy(row => row.Outcome.Should().Be(AuditOutcome.Succeeded));
    }

    internal static CreateCredentialProfileRequest SnmpV3Request(string name, string? description = null) =>
        new(
            name,
            CredentialKind.SnmpV3,
            new CredentialMaterial(AuthPassword: "authentication-pass-phrase", PrivacyPassword: "privacy-phrase"),
            Description: description,
            Username: "netshield",
            AuthAlgorithm: SnmpAuthAlgorithm.Sha256,
            PrivacyAlgorithm: SnmpPrivacyAlgorithm.Aes128);

    internal static async Task<Guid> CreateAsync(
        InventoryHost host,
        string name,
        CancellationToken cancellationToken,
        string? description = null)
    {
        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            SnmpV3Request(name, description),
            cancellationToken);

        created.Status.Should().Be(201, "the profile has to exist before the test can act on it");

        return Read(created).Id;
    }

    internal static async Task<Guid> CreateCommunityAsync(
        InventoryHost host,
        string name,
        CancellationToken cancellationToken)
    {
        ApiResponse created = await host.Client.PostAsync(
            Profiles,
            new CreateCredentialProfileRequest(
                name,
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: "read-only")),
            cancellationToken);

        created.Status.Should().Be(201);

        return Read(created).Id;
    }

    internal static CredentialProfileDetail Read(ApiResponse response) =>
        System.Text.Json.JsonSerializer.Deserialize<CredentialProfileDetail>(
            response.Body,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
}
