using FluentAssertions;

using NetShield.Contracts.Collector;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Collector.Contract;

namespace NetShield.UnitTests.Collector;

/// <summary>
/// The shape rules the internal contract's boundary enforces. Everything about whether a job
/// exists, whether this collector still holds it, and whether it has already been reported on is
/// a fact about stored state and lives in the handler.
/// </summary>
public sealed class CollectorValidatorTests
{
    private static readonly CollectorResultReport AnyReport =
        new(Guid.CreateVersion7(), "token", CollectorJobOutcome.Succeeded, null, null);

    [Fact]
    public void Heartbeat_WithANameAndSaneCounts_IsValid() =>
        new CollectorHeartbeatRequestValidator()
            .Validate(new CollectorHeartbeatRequest("collector-1", "0.1.0", 8, 3))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Heartbeat_WithNoName_IsInvalid() =>
        new CollectorHeartbeatRequestValidator()
            .Validate(new CollectorHeartbeatRequest("  ", "0.1.0", 8, 3))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Heartbeat_WithANameOverTheColumnWidth_IsInvalid() =>
        new CollectorHeartbeatRequestValidator()
            .Validate(new CollectorHeartbeatRequest(
                new string('c', CollectorLimits.NameLength + 1),
                "0.1.0",
                8,
                3))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Heartbeat_WithNoVersion_IsValid() =>
        new CollectorHeartbeatRequestValidator()
            .Validate(new CollectorHeartbeatRequest("collector-1", null, 8, 3))
            .IsValid.Should().BeTrue("a collector that did not say which build it is is still alive");

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(10_001, 0)]
    public void Heartbeat_WithANonsenseCount_IsInvalid(int capacity, int running) =>
        new CollectorHeartbeatRequestValidator()
            .Validate(new CollectorHeartbeatRequest("collector-1", "0.1.0", capacity, running))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Results_WithOneWellFormedReport_IsValid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest("collector-1", [AnyReport]))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Results_WithNoCollectorNamed_IsInvalid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest("", [AnyReport]))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Results_WithNoReports_IsValid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest("collector-1", []))
            .IsValid.Should().BeTrue("an empty batch is a wasted round trip, not a malformed one");

    [Fact]
    public void Results_WithAReportCarryingNoLeaseToken_IsInvalid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest(
                "collector-1",
                [AnyReport with { LeaseToken = "" }]))
            .IsValid.Should().BeFalse("the token is what makes the submission idempotent");

    [Fact]
    public void Results_WithAReportNamingNoJob_IsInvalid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest(
                "collector-1",
                [AnyReport with { JobId = Guid.Empty }]))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Results_WithADetailOverTheColumnWidth_IsInvalid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest(
                "collector-1",
                [AnyReport with { Detail = new string('d', CollectorLimits.DetailLength + 1) }]))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Results_WithAnOutcomeThatIsNotAMember_IsInvalid() =>
        new CollectorResultsRequestValidator()
            .Validate(new CollectorResultsRequest(
                "collector-1",
                [AnyReport with { Outcome = (CollectorJobOutcome)99 }]))
            .IsValid.Should().BeFalse();
}
