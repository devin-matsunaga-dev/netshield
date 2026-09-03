using System.Text.Json.Serialization;

namespace NetShield.Contracts.Identity;

/// <summary>
/// The four roles NetShield recognises (SPEC.md §2, Administration).
/// </summary>
/// <remarks>
/// <para>
/// WP-0.4 stores this on the user and emits it as a session claim, and does nothing else with
/// it. There is no permission mapping, no hierarchy, and no endpoint gating until WP-0.5 —
/// deliberately, so that the column exists before the first administrator is seeded and RBAC
/// is designed once rather than grown by accident.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal, so that adding a role in WP-0.5 cannot
/// renumber what a stored response, a generated client or a saved fixture already means.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole
{
    /// <summary>Full access, including user and platform administration.</summary>
    Administrator,

    /// <summary>Day-to-day operation: inventory, alerts, configuration reads.</summary>
    Operator,

    /// <summary>Investigation across telemetry, flows, logs and findings.</summary>
    Analyst,

    /// <summary>Sees everything permitted, changes nothing.</summary>
    ReadOnly
}
