using FluentAssertions;

using FluentValidation.Results;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Endpoints;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The shape rules the endpoint boundary enforces (CONVENTIONS.md §4). Everything about what a
/// kind requires is a semantic rule and lives in <c>CredentialKindRules</c>.
/// </summary>
public sealed class CredentialRequestValidatorTests
{
    private static readonly CredentialMaterial AnyMaterial = new(Community: "read-only");

    [Fact]
    public void Create_WithANameAndMaterial_IsValid() =>
        new CreateCredentialProfileRequestValidator()
            .Validate(new CreateCredentialProfileRequest("Core read-only", CredentialKind.SnmpV2c, AnyMaterial))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Create_WithNoName_IsInvalid() =>
        new CreateCredentialProfileRequestValidator()
            .Validate(new CreateCredentialProfileRequest("  ", CredentialKind.SnmpV2c, AnyMaterial))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Create_WithANameOverTheColumnWidth_IsInvalid() =>
        new CreateCredentialProfileRequestValidator()
            .Validate(new CreateCredentialProfileRequest(
                new string('n', CredentialLimits.NameLength + 1),
                CredentialKind.SnmpV2c,
                AnyMaterial))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Create_WithNoMaterial_IsInvalid() =>
        new CreateCredentialProfileRequestValidator()
            .Validate(new CreateCredentialProfileRequest("Core", CredentialKind.SnmpV2c, null!))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Create_WithACommunityOverTheLimit_IsInvalid() =>
        new CreateCredentialProfileRequestValidator()
            .Validate(new CreateCredentialProfileRequest(
                "Core",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: new string('c', CredentialLimits.SecretLength + 1))))
            .IsValid.Should().BeFalse();

    /// <summary>
    /// FluentValidation interpolates the property value into its default message. A rejection
    /// that quoted the community string back would be SPEC.md §5 broken by a helpful error, so
    /// every rule on the material carries a message of its own.
    /// </summary>
    [Fact]
    public void Create_WhenAMaterialRuleFails_TheMessageDoesNotQuoteTheSecret()
    {
        const string Secret = "a-community-nobody-should-see";

        ValidationResult result = new CreateCredentialProfileRequestValidator()
            .Validate(new CreateCredentialProfileRequest(
                "Core",
                CredentialKind.SnmpV2c,
                new CredentialMaterial(Community: Secret.PadRight(CredentialLimits.SecretLength + 1, 'x'))));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().AllSatisfy(error => error.ErrorMessage.Should().NotContain(Secret));
    }

    [Fact]
    public void Update_WithANameAlone_IsValid() =>
        new UpdateCredentialProfileRequestValidator()
            .Validate(new UpdateCredentialProfileRequest("Core read-only"))
            .IsValid.Should().BeTrue();

    [Fact]
    public void ReplaceMaterial_WithNoMaterial_IsInvalid() =>
        new ReplaceCredentialMaterialRequestValidator()
            .Validate(new ReplaceCredentialMaterialRequest(null!))
            .IsValid.Should().BeFalse();

    [Fact]
    public void ReplaceMaterial_WithAPrivateKeyInsideTheLimit_IsValid() =>
        new ReplaceCredentialMaterialRequestValidator()
            .Validate(new ReplaceCredentialMaterialRequest(
                new CredentialMaterial(PrivateKey: new string('k', CredentialLimits.PrivateKeyLength))))
            .IsValid.Should().BeTrue();

    /// <summary>An empty list is how a caller unassigns everything, and it has to be accepted.</summary>
    [Fact]
    public void SetAssignments_WithAnEmptyList_IsValid() =>
        new SetDeviceCredentialProfilesRequestValidator()
            .Validate(new SetDeviceCredentialProfilesRequest([]))
            .IsValid.Should().BeTrue();

    [Fact]
    public void SetAssignments_WithMoreThanTheLimit_IsInvalid() =>
        new SetDeviceCredentialProfilesRequestValidator()
            .Validate(new SetDeviceCredentialProfilesRequest(
                [.. Enumerable.Range(0, CredentialLimits.MaximumAssignmentsPerDevice + 1)
                    .Select(_ => Guid.CreateVersion7())]))
            .IsValid.Should().BeFalse();

    [Fact]
    public void SetAssignments_WithAnEmptyId_IsInvalid() =>
        new SetDeviceCredentialProfilesRequestValidator()
            .Validate(new SetDeviceCredentialProfilesRequest([Guid.Empty]))
            .IsValid.Should().BeFalse();
}
