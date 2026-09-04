using System.ComponentModel.DataAnnotations;

namespace NetShield.Platform.Authentication;

/// <summary>
/// The shared secret <c>netshield-collector</c> presents on the internal contract
/// (ARCHITECTURE.md §7).
/// </summary>
/// <remarks>
/// <para>
/// Supplied by configuration and never by a default. There is no fallback value and no
/// development exemption: a host that has not been given a secret fails at startup rather than
/// serving <c>/internal</c> to whoever asks, which is the failure mode a default would create on
/// the one installation that forgot to set it.
/// </para>
/// <para>
/// It is one secret for the whole collector fleet in V1. Per-collector credentials would need an
/// enrolment story, a revocation story and a table, and SPEC.md §1 describes one administrator
/// running one estate — the secret is scoped by the network the contract is bound to, which is
/// what ARCHITECTURE.md §7 asks for.
/// </para>
/// </remarks>
public sealed class CollectorAuthenticationOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Collector";

    /// <summary>
    /// The shortest secret the host will start with. Long enough that guessing it is not a
    /// strategy, and the AppHost and any deployment generate rather than choose one.
    /// </summary>
    public const int MinimumSecretLength = 32;

    /// <summary>
    /// The bearer token the collector presents. Compared in fixed time, never logged, and never
    /// returned by any endpoint.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumSecretLength)]
    public string SharedSecret { get; set; } = string.Empty;
}
