using System.ComponentModel.DataAnnotations;

using FluentAssertions;

using NetShield.Inventory.Discovery;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The discovery settings' defaults and their bounds. They are validated on start, so a
/// deployment that sets one of them wrongly fails to come up rather than walking devices oddly.
/// </summary>
public sealed class DiscoveryOptionsTests
{
    [Fact]
    public void Defaults_AreTheOnesTheWalkIsDesignedAround()
    {
        DiscoveryOptions options = new();

        options.RequestTimeoutSeconds.Should().Be(5);
        options.Retries.Should().Be(2);
        options.MaxRepetitions.Should().Be(25);
        options.MaxRowsPerSubtree.Should().Be(20_000);
        options.MaxInterfaces.Should().Be(512);
    }

    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new DiscoveryOptions()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void RequestTimeout_OutsideItsRange_IsRefused(double seconds)
    {
        Validate(new DiscoveryOptions { RequestTimeoutSeconds = seconds })
            .Should().ContainSingle();
    }

    [Fact]
    public void MaxInterfaces_StaysWithinTheResultPayloadCeiling()
    {
        // CollectorLimits.ResultLength is 256 KiB and an interface is a couple of hundred bytes
        // of JSON. The default has to leave that room, or a walk of a large device is refused at
        // submission and wasted.
        const int BytesPerInterface = 256;

        (new DiscoveryOptions().MaxInterfaces * BytesPerInterface).Should().BeLessThan(256 * 1024);
    }

    private static IReadOnlyList<ValidationResult> Validate(DiscoveryOptions options)
    {
        List<ValidationResult> results = [];

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
