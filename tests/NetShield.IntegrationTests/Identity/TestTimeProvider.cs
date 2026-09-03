namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// A clock the test moves by hand, so that a lockout window and a token lifetime can be crossed
/// without the suite waiting for them.
/// </summary>
internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan amount) => _utcNow += amount;
}
