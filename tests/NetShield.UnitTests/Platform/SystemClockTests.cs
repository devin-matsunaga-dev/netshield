using FluentAssertions;

using NetShield.Platform.Time;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers the injected clock. CONVENTIONS.md §3: never a local time, anywhere.
/// </summary>
public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReadsTheTimeProvider()
    {
        DateTimeOffset instant = new(2026, 9, 3, 10, 30, 0, TimeSpan.Zero);

        new SystemClock(new FixedTimeProvider(instant)).UtcNow.Should().Be(instant);
    }

    [Fact]
    public void UtcNow_IsUtc_EvenWhenTheProviderIsOffset()
    {
        DateTimeOffset localMidday = new(2026, 9, 3, 12, 0, 0, TimeSpan.FromHours(9));

        DateTimeOffset now = new SystemClock(new FixedTimeProvider(localMidday)).UtcNow;

        now.Offset.Should().Be(TimeSpan.Zero, "a stored timestamp is UTC or it is a bug");
        now.UtcDateTime.Should().Be(new DateTime(2026, 9, 3, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void SystemProvider_ReportsTheCurrentInstant()
    {
        new SystemClock(TimeProvider.System).UtcNow.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
