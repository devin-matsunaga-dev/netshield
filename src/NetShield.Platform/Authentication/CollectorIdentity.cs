namespace NetShield.Platform.Authentication;

/// <summary>
/// The names the collector contract is identified by: its authentication scheme, its
/// authorization policy, and the principal a successful presentation of the shared secret
/// produces.
/// </summary>
public static class CollectorIdentity
{
    /// <summary>The authentication scheme the internal endpoints accept.</summary>
    public const string Scheme = "NetShield.Collector";

    /// <summary>The authorization policy the internal endpoints require.</summary>
    public const string PolicyName = "netshield:collector";

    /// <summary>
    /// The name on the principal. It is the fleet, not a person and not a machine: the shared
    /// secret says "a collector", and which one is a claim the heartbeat body makes rather than
    /// something the credential proves.
    /// </summary>
    public const string PrincipalName = "collector";

    /// <summary>
    /// What an audit row from the collector contract calls the thing that received a credential.
    /// </summary>
    public const string ActorLabel = "collector";
}
