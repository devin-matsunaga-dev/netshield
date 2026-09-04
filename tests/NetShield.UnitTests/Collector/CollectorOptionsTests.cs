using System.ComponentModel.DataAnnotations;

using FluentAssertions;

using NetShield.Inventory.Collector;

using NetShield.Platform.Authentication;

namespace NetShield.UnitTests.Collector;

/// <summary>
/// The two option sets the host refuses to start without, and the bounds they hold.
/// </summary>
/// <remarks>
/// Both are validated on start rather than on first use, for the reason the key ring is: a host
/// that came up and only failed on the first collector request would pass its health checks and
/// look fine (WP-1.2).
/// </remarks>
public sealed class CollectorOptionsTests
{
    [Fact]
    public void ASecretOfSufficientLength_IsAccepted() =>
        Validate(new CollectorAuthenticationOptions
        {
            SharedSecret = new string('s', CollectorAuthenticationOptions.MinimumSecretLength)
        }).Should().BeEmpty();

    [Fact]
    public void AnEmptySecret_IsRefused() =>
        Validate(new CollectorAuthenticationOptions { SharedSecret = string.Empty })
            .Should().NotBeEmpty("there is no default and no development fallback");

    [Fact]
    public void AShortSecret_IsRefused() =>
        Validate(new CollectorAuthenticationOptions
        {
            SharedSecret = new string('s', CollectorAuthenticationOptions.MinimumSecretLength - 1)
        }).Should().NotBeEmpty("a guessable secret is the whole of the internal contract's defence");

    [Fact]
    public void TheDefaults_AreValid() => Validate(new CollectorJobOptions()).Should().BeEmpty();

    [Fact]
    public void TheDefaultLease_IsLongerThanTheDefaultPoll()
    {
        CollectorJobOptions options = new();

        options.LeaseSeconds.Should().BeGreaterThan(
            options.PollSeconds,
            "a lease that expired before the next poll would hand every job out twice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(3601)]
    public void ALeaseOutsideTheBounds_IsRefused(int seconds) =>
        Validate(new CollectorJobOptions { LeaseSeconds = seconds }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ABatchCeilingOutsideTheBounds_IsRefused(int jobs) =>
        Validate(new CollectorJobOptions { MaxJobsPerLease = jobs }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void AnAttemptLimitOutsideTheBounds_IsRefused(int attempts) =>
        Validate(new CollectorJobOptions { MaxAttempts = attempts }).Should().NotBeEmpty();

    private static IReadOnlyList<ValidationResult> Validate(object options)
    {
        List<ValidationResult> results = [];

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
