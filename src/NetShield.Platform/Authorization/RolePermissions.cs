using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// What each role may do. The whole of NetShield's RBAC policy, in one table.
/// </summary>
/// <remarks>
/// <para>
/// The session cookie carries a role and nothing else. Permissions are resolved here, on every
/// request, so that a change to what a role may do takes effect for sessions that are already
/// open — and so that a client which forges a permission claim has forged something nothing
/// reads (ARCHITECTURE.md §8).
/// </para>
/// <para>
/// The shape of the table: <em>Administrator</em> holds everything. <em>Operator</em> runs the
/// estate day to day but holds neither credentials nor platform administration.
/// <em>Analyst</em> investigates — every read, plus incident triage and the findings and reports
/// that come out of it — and changes no inventory. <em>Read-only</em> holds the reads and
/// nothing else.
/// </para>
/// </remarks>
public static class RolePermissions
{
    private static readonly Permission[] AllPermissions = Enum.GetValues<Permission>();

    private static readonly IReadOnlySet<Permission> ReadOnlyPermissions = new HashSet<Permission>
    {
        Permission.InventoryRead,
        Permission.TopologyRead,
        Permission.TelemetryRead,
        Permission.FlowsRead,
        Permission.LogsRead,
        Permission.AlertsRead,
        Permission.ConfigsRead,
        Permission.ComplianceRead,
        Permission.VulnerabilitiesRead,
        Permission.ReportsRead
    };

    private static readonly IReadOnlySet<Permission> AnalystPermissions = new HashSet<Permission>(ReadOnlyPermissions)
    {
        Permission.AlertsManage,
        Permission.VulnerabilitiesManage,
        Permission.ReportsManage
    };

    private static readonly IReadOnlySet<Permission> OperatorPermissions = new HashSet<Permission>(ReadOnlyPermissions)
    {
        Permission.InventoryWrite,
        Permission.DiscoveryRun,
        Permission.AlertsManage,
        Permission.AlertRulesWrite,
        Permission.ConfigsManage,
        Permission.ComplianceManage,
        Permission.VulnerabilitiesManage,
        Permission.ReportsManage,
        Permission.PoliciesWrite
    };

    private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<Permission>> Table =
        new Dictionary<UserRole, IReadOnlySet<Permission>>
        {
            [UserRole.Administrator] = new HashSet<Permission>(AllPermissions),
            [UserRole.Operator] = OperatorPermissions,
            [UserRole.Analyst] = AnalystPermissions,
            [UserRole.ReadOnly] = ReadOnlyPermissions
        };

    /// <summary>Every permission <paramref name="role"/> holds.</summary>
    public static IReadOnlySet<Permission> For(UserRole role) =>
        Table.TryGetValue(role, out IReadOnlySet<Permission>? permissions)
            ? permissions
            : throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Every UserRole must appear in the permission table; a role with no entry would "
                + "otherwise silently hold nothing.");

    /// <summary>Whether <paramref name="role"/> may do <paramref name="permission"/>.</summary>
    public static bool Grants(UserRole role, Permission permission) => For(role).Contains(permission);
}
