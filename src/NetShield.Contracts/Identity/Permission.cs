using System.Text.Json.Serialization;

namespace NetShield.Contracts.Identity;

/// <summary>
/// The capabilities a NetShield session may hold. One member per thing a user can do, at the
/// grain SPEC.md §2 draws its areas at.
/// </summary>
/// <remarks>
/// <para>
/// A permission is never carried on the wire as a claim and never trusted from the client. It is
/// resolved on the server from the session's role through <c>RolePermissions</c>
/// (ARCHITECTURE.md §8), so that changing what a role may do is one edit in one table rather
/// than a re-issue of every live session.
/// </para>
/// <para>
/// The read/write split is deliberate and coarse. A finer grain would be guesswork before the
/// modules that need it exist; adding a member later costs nothing, while splitting one that a
/// role map and a dozen endpoints already name costs a migration of intent.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored response, a generated client or a saved fixture already means.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<Permission>))]
public enum Permission
{
    /// <summary>Read devices, clients, sites and their manual attributes.</summary>
    InventoryRead,

    /// <summary>Create, change and remove devices and their attributes.</summary>
    InventoryWrite,

    /// <summary>
    /// Create, rotate and revoke device credential profiles. Held by the Administrator alone:
    /// credential lifecycle is the highest-blast-radius privilege in the system, and least
    /// privilege says routine operation should not carry it.
    /// </summary>
    CredentialsManage,

    /// <summary>Start a discovery run outside its schedule.</summary>
    DiscoveryRun,

    /// <summary>Read the topology graph and the VLAN inventory.</summary>
    TopologyRead,

    /// <summary>Read metric series and device health rollups.</summary>
    TelemetryRead,

    /// <summary>Read flow records and their aggregations.</summary>
    FlowsRead,

    /// <summary>Search and read normalised log events and per-source ingest health.</summary>
    LogsRead,

    /// <summary>Read alerts, incidents and their history.</summary>
    AlertsRead,

    /// <summary>Acknowledge, assign and resolve incidents.</summary>
    AlertsManage,

    /// <summary>Create, change and remove alert rules.</summary>
    AlertRulesWrite,

    /// <summary>Read config backups, versions and diffs.</summary>
    ConfigsRead,

    /// <summary>Trigger a backup and maintain the golden templates drift is measured against.</summary>
    ConfigsManage,

    /// <summary>Read baselines, assessment results and their evidence.</summary>
    ComplianceRead,

    /// <summary>Author baselines and custom rules, and start an assessment.</summary>
    ComplianceManage,

    /// <summary>Read imported findings and their correlation to assets.</summary>
    VulnerabilitiesRead,

    /// <summary>Import scanner output and maintain remediation status.</summary>
    VulnerabilitiesManage,

    /// <summary>Read report definitions and generated reports.</summary>
    ReportsRead,

    /// <summary>Author report definitions, run one, and schedule it.</summary>
    ReportsManage,

    /// <summary>
    /// Change retention policies, notification routing, discovery schedules and maintenance
    /// windows — the settings that decide what the platform does on its own.
    /// </summary>
    PoliciesWrite,

    /// <summary>Read the append-only audit log.</summary>
    AuditRead,

    /// <summary>
    /// Administer the platform itself: accounts, roles, SSO, system health, backup and restore.
    /// </summary>
    SystemAdminister
}
