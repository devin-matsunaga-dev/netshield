using FluentAssertions;

using NetShield.Platform.Auditing;

namespace NetShield.UnitTests.Auditing;

/// <summary>Covers how a status code becomes the word an operator scans the audit log for.</summary>
public sealed class AuditOutcomesTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(302)]
    public void FromStatusCode_ACallThatWorked_Succeeded(int status) =>
        AuditOutcomes.FromStatusCode(status).Should().Be(AuditOutcome.Succeeded);

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void FromStatusCode_ARefusal_IsDeniedRatherThanFailed(int status) =>
        // "This account tried to do something it is not allowed to do" is the line an audit log
        // exists for, and it reads nothing like a validation failure.
        AuditOutcomes.FromStatusCode(status).Should().Be(AuditOutcome.Denied);

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(429)]
    [InlineData(500)]
    public void FromStatusCode_EveryOtherRejection_IsFailed(int status) =>
        AuditOutcomes.FromStatusCode(status).Should().Be(AuditOutcome.Failed);
}
