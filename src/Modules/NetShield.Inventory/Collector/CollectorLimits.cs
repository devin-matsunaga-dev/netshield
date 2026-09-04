namespace NetShield.Inventory.Collector;

/// <summary>
/// The lengths the collector contract's columns and validators agree on.
/// </summary>
/// <remarks>
/// One place, so that a validator and the column it protects cannot drift — the same reason
/// <c>DeviceLimits</c> and <c>CredentialLimits</c> exist.
/// </remarks>
internal static class CollectorLimits
{
    /// <summary>A collector's self-reported name.</summary>
    internal const int NameLength = 128;

    /// <summary>A collector's self-reported version string.</summary>
    internal const int VersionLength = 64;

    /// <summary>A lease token. A UUID in <c>N</c> form is 32 characters.</summary>
    internal const int LeaseTokenLength = 64;

    /// <summary>
    /// The sentence a collector attaches to a result. Long enough for a socket error and a
    /// device name, short enough that a stack trace cannot be pasted into the queue.
    /// </summary>
    internal const int DetailLength = 512;

    /// <summary>
    /// The largest result payload the API will store for one job. A collector that has more to
    /// say than this is reporting something WP-1.3 did not design for, and truncating it
    /// silently would be worse than refusing it.
    /// </summary>
    internal const int ResultLength = 256 * 1024;

    /// <summary>The largest parameter document a queued job may carry.</summary>
    internal const int ParametersLength = 16 * 1024;

    /// <summary>The most results one submission may carry.</summary>
    internal const int MaxResultsPerSubmission = 100;
}
