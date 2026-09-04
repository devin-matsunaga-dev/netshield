using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;

using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// What each credential kind requires — the semantic rules that answer 422, as distinct from the
/// shape rules that answer 400.
/// </summary>
public sealed class CredentialKindRulesTests
{
    [Fact]
    public void CheckAttributes_ForSnmpV2c_WithNothingButTheKind_Succeeds() =>
        CredentialKindRules.CheckAttributes(CredentialKind.SnmpV2c, null, null, null)
            .IsSuccess.Should().BeTrue();

    /// <summary>
    /// A v2c community has no user behind it. Accepting one and storing it would put a username
    /// on a profile nothing will ever read it from.
    /// </summary>
    [Fact]
    public void CheckAttributes_ForSnmpV2c_WithAUsername_IsRefused()
    {
        Result result = CredentialKindRules.CheckAttributes(CredentialKind.SnmpV2c, "operator", null, null);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.Unprocessable);
        result.Error!.Code.Should().Be(CredentialErrors.AttributesInvalidCode);
    }

    [Theory]
    [InlineData(CredentialKind.SshPassword)]
    [InlineData(CredentialKind.SshKey)]
    public void CheckAttributes_ForSsh_WithoutAUsername_IsRefused(CredentialKind kind) =>
        CredentialKindRules.CheckAttributes(kind, null, null, null).IsSuccess.Should().BeFalse();

    [Theory]
    [InlineData(CredentialKind.SshPassword)]
    [InlineData(CredentialKind.SshKey)]
    public void CheckAttributes_ForSsh_WithSnmpAlgorithms_IsRefused(CredentialKind kind) =>
        CredentialKindRules.CheckAttributes(kind, "operator", SnmpAuthAlgorithm.Sha256, null)
            .IsSuccess.Should().BeFalse();

    [Fact]
    public void CheckAttributes_ForSnmpV3_WithBothAlgorithmsAndASecurityName_Succeeds() =>
        CredentialKindRules.CheckAttributes(
                CredentialKind.SnmpV3,
                "netshield",
                SnmpAuthAlgorithm.Sha256,
                SnmpPrivacyAlgorithm.Aes128)
            .IsSuccess.Should().BeTrue();

    /// <summary>
    /// authNoPriv is a decision, not an omission. It is expressed as privacy None rather than as
    /// a missing algorithm, so that a compliance rule in Phase 7 can read what was chosen.
    /// </summary>
    [Fact]
    public void CheckAttributes_ForSnmpV3_WithPrivacyNone_Succeeds() =>
        CredentialKindRules.CheckAttributes(
                CredentialKind.SnmpV3,
                "netshield",
                SnmpAuthAlgorithm.Sha256,
                SnmpPrivacyAlgorithm.None)
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void CheckAttributes_ForSnmpV3_WithoutAPrivacyAlgorithm_IsRefused() =>
        CredentialKindRules.CheckAttributes(CredentialKind.SnmpV3, "netshield", SnmpAuthAlgorithm.Sha256, null)
            .IsSuccess.Should().BeFalse();

    [Fact]
    public void CheckMaterial_ForSnmpV2c_WithACommunity_Succeeds() =>
        CredentialKindRules.CheckMaterial(
                CredentialKind.SnmpV2c,
                null,
                new CredentialMaterialPayload { Community = "read-only" })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void CheckMaterial_ForSnmpV2c_WithNoCommunity_NamesWhatIsMissing()
    {
        Result result = CredentialKindRules.CheckMaterial(
            CredentialKind.SnmpV2c,
            null,
            new CredentialMaterialPayload());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(CredentialErrors.MaterialIncompleteCode);
        result.Error!.Message.Should().Contain("community");
    }

    /// <summary>
    /// A member belonging to another kind is refused rather than dropped. Storing it would accept
    /// a request that says one thing and keep another, with nothing to tell the caller.
    /// </summary>
    [Fact]
    public void CheckMaterial_WithAMemberFromAnotherKind_IsRefused()
    {
        Result result = CredentialKindRules.CheckMaterial(
            CredentialKind.SnmpV2c,
            null,
            new CredentialMaterialPayload { Community = "read-only", Password = "not-here" });

        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("password");
    }

    [Fact]
    public void CheckMaterial_ForSnmpV3_WithPrivacy_NeedsBothPassPhrases()
    {
        CredentialKindRules.CheckMaterial(
                CredentialKind.SnmpV3,
                SnmpPrivacyAlgorithm.Aes128,
                new CredentialMaterialPayload { AuthPassword = "auth" })
            .IsSuccess.Should().BeFalse();

        CredentialKindRules.CheckMaterial(
                CredentialKind.SnmpV3,
                SnmpPrivacyAlgorithm.Aes128,
                new CredentialMaterialPayload { AuthPassword = "auth", PrivacyPassword = "priv" })
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// With privacy None the privacy pass phrase is not merely optional, it is wrong: nothing
    /// would ever use it and storing it would suggest the profile encrypts when it does not.
    /// </summary>
    [Fact]
    public void CheckMaterial_ForSnmpV3_WithPrivacyNone_RefusesAPrivacyPassPhrase()
    {
        CredentialKindRules.CheckMaterial(
                CredentialKind.SnmpV3,
                SnmpPrivacyAlgorithm.None,
                new CredentialMaterialPayload { AuthPassword = "auth" })
            .IsSuccess.Should().BeTrue();

        CredentialKindRules.CheckMaterial(
                CredentialKind.SnmpV3,
                SnmpPrivacyAlgorithm.None,
                new CredentialMaterialPayload { AuthPassword = "auth", PrivacyPassword = "priv" })
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void CheckMaterial_ForSshPassword_NeedsThePassword() =>
        CredentialKindRules.CheckMaterial(
                CredentialKind.SshPassword,
                null,
                new CredentialMaterialPayload { Password = "letmein" })
            .IsSuccess.Should().BeTrue();

    /// <summary>A private key may or may not be protected, and both are ordinary.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("protected")]
    public void CheckMaterial_ForSshKey_AcceptsAPassPhraseOrNone(string? passPhrase) =>
        CredentialKindRules.CheckMaterial(
                CredentialKind.SshKey,
                null,
                new CredentialMaterialPayload
                {
                    PrivateKey = "-----BEGIN PRIVATE KEY-----",
                    PrivateKeyPassword = passPhrase
                })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void CheckMaterial_ForSshKey_WithoutTheKey_IsRefused() =>
        CredentialKindRules.CheckMaterial(
                CredentialKind.SshKey,
                null,
                new CredentialMaterialPayload { PrivateKeyPassword = "protected" })
            .IsSuccess.Should().BeFalse();

    /// <summary>
    /// A secret that arrived as whitespace is absent, so it is refused as missing rather than
    /// stored as a credential nobody can authenticate with.
    /// </summary>
    [Fact]
    public void From_TreatsAWhitespaceOnlySecretAsAbsent() =>
        CredentialMaterialPayload.From(new CredentialMaterial(Community: "   ")).Community.Should().BeNull();

    /// <summary>
    /// A pass phrase may legitimately begin or end with a space. Trimming one silently is how a
    /// credential that was typed correctly stops working, on a device nobody can reach to check.
    /// </summary>
    [Fact]
    public void From_DoesNotTrimASecretThatHasContent() =>
        CredentialMaterialPayload.From(new CredentialMaterial(Password: " spaced "))
            .Password.Should().Be(" spaced ");

    /// <summary>PEM is line-oriented, and trimming it is how a key stops parsing.</summary>
    [Fact]
    public void From_LeavesAPrivateKeyExactlyAsItArrived()
    {
        const string Pem = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n";

        CredentialMaterialPayload.From(new CredentialMaterial(PrivateKey: Pem)).PrivateKey.Should().Be(Pem);
    }
}
