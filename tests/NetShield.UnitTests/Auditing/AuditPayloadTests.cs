using System.Text.Json;

using FluentAssertions;

using NetShield.Platform.Auditing;
using NetShield.Platform.Logging;

namespace NetShield.UnitTests.Auditing;

/// <summary>
/// Covers what a before/after snapshot turns into on its way to the database. SPEC.md §5 covers
/// the database as well as the log, and an append-only table is the one place a leaked secret
/// can never be taken back out.
/// </summary>
public sealed class AuditPayloadTests
{
    private readonly SecretRedactor _redactor = new();

    [Fact]
    public void Serialize_NoState_IsNull() =>
        AuditPayload.Serialize(null, _redactor).Should().BeNull();

    [Fact]
    public void Serialize_AnEmptySnapshot_IsNull() =>
        // A null column reads better than an empty object, and reads the same as "nothing said".
        AuditPayload.Serialize(new Dictionary<string, object?>(), _redactor).Should().BeNull();

    [Fact]
    public void Serialize_KeepsTheMemberNamesAndTypesItWasGiven()
    {
        string? json = AuditPayload.Serialize(
            new Dictionary<string, object?> { ["changeRequired"] = false, ["attempts"] = 3 },
            _redactor);

        JsonElement element = JsonDocument.Parse(json!).RootElement;

        element.GetProperty("changeRequired").GetBoolean().Should().BeFalse();
        element.GetProperty("attempts").GetInt32().Should().Be(3);
    }

    [Fact]
    public void Serialize_AHarmlessValueUnderASecretShapedName_IsStillRedacted()
    {
        // The name rule wins outright and does not stop to consider that a boolean cannot be a
        // password. That is the intended trade — a false redaction costs a debugging session,
        // a missed one costs a credential — and it is why a snapshot names the fact that changed
        // rather than the credential it changed about: "changeRequired", not
        // "mustChangePassword".
        string? json = AuditPayload.Serialize(
            new Dictionary<string, object?> { ["mustChangePassword"] = false },
            _redactor);

        JsonDocument.Parse(json!).RootElement
            .GetProperty("mustChangePassword").GetString().Should().Be(SecretRedactor.Placeholder);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("newPassword")]
    [InlineData("apiKey")]
    [InlineData("snmpCommunity")]
    [InlineData("privateKey")]
    [InlineData("refreshToken")]
    public void Serialize_APropertyWhoseNameReadsAsASecret_LosesItsValue(string member)
    {
        string? json = AuditPayload.Serialize(
            new Dictionary<string, object?> { [member] = "hunter2-correct-horse" },
            _redactor);

        json.Should().NotContain("hunter2-correct-horse");
        json.Should().Contain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void Serialize_ASecretShapeInsideAnInnocentProperty_LosesThatPart()
    {
        string? json = AuditPayload.Serialize(
            new Dictionary<string, object?> { ["notes"] = "set community=public on the switch" },
            _redactor);

        json.Should().NotContain("public");
        json.Should().Contain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void Serialize_ANullValue_IsKeptAsNull()
    {
        string? json = AuditPayload.Serialize(
            new Dictionary<string, object?> { ["lockedOutUntil"] = null },
            _redactor);

        JsonDocument.Parse(json!).RootElement
            .GetProperty("lockedOutUntil").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
