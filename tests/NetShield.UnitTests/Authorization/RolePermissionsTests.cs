using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Authorization;

/// <summary>
/// Covers the whole of NetShield's RBAC policy. CONVENTIONS.md §7 names RBAC checks as an area
/// that must be tested before a package closes, and this table is the check.
/// </summary>
public sealed class RolePermissionsTests
{
    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public void For_EveryRole_ReturnsANonEmptySet(UserRole role) =>
        RolePermissions.For(role).Should().NotBeEmpty();

    [Fact]
    public void For_ARoleOutsideTheEnum_Throws()
    {
        // A role added to UserRole without an entry here would otherwise silently hold nothing,
        // which reads in production as "the feature is broken" rather than "RBAC is incomplete".
        Action act = () => RolePermissions.For((UserRole)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Administrator_HoldsEveryPermission() =>
        RolePermissions.For(UserRole.Administrator).Should().BeEquivalentTo(Enum.GetValues<Permission>());

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public void OnlyTheAdministrator_HoldsCredentialsAndSystemAdministration(UserRole role)
    {
        RolePermissions.Grants(role, Permission.CredentialsManage).Should().BeFalse(
            "credential lifecycle is the highest-blast-radius privilege in the system");

        RolePermissions.Grants(role, Permission.SystemAdminister).Should().BeFalse();
        RolePermissions.Grants(role, Permission.AuditRead).Should().BeFalse();
    }

    [Theory]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public void NeitherTheAnalystNorReadOnly_MayWriteInventory(UserRole role) =>
        RolePermissions.Grants(role, Permission.InventoryWrite).Should().BeFalse();

    [Fact]
    public void TheOperator_RunsTheEstateWithoutAdministeringThePlatform()
    {
        IReadOnlySet<Permission> permissions = RolePermissions.For(UserRole.Operator);

        permissions.Should().Contain([
            Permission.InventoryWrite,
            Permission.DiscoveryRun,
            Permission.AlertRulesWrite,
            Permission.ConfigsManage,
            Permission.PoliciesWrite
        ]);
    }

    [Fact]
    public void TheAnalyst_InvestigatesButChangesNoInventoryAndNoRules()
    {
        IReadOnlySet<Permission> permissions = RolePermissions.For(UserRole.Analyst);

        permissions.Should().Contain([
            Permission.LogsRead,
            Permission.FlowsRead,
            Permission.AlertsManage,
            Permission.VulnerabilitiesManage,
            Permission.ReportsManage
        ]);

        permissions.Should().NotContain([
            Permission.InventoryWrite,
            Permission.AlertRulesWrite,
            Permission.ConfigsManage,
            Permission.PoliciesWrite,
            Permission.DiscoveryRun
        ]);
    }

    [Fact]
    public void ReadOnly_HoldsNothingThatIsNotARead()
    {
        IReadOnlySet<Permission> permissions = RolePermissions.For(UserRole.ReadOnly);

        permissions.Should().OnlyContain(permission => permission.ToString().EndsWith("Read", StringComparison.Ordinal));
        permissions.Should().NotContain(Permission.AuditRead, "the audit log is administration");
    }

    [Fact]
    public void EveryRoleBelowAdministrator_HoldsStrictlyLess()
    {
        IReadOnlySet<Permission> administrator = RolePermissions.For(UserRole.Administrator);

        foreach (UserRole role in Enum.GetValues<UserRole>().Where(role => role != UserRole.Administrator))
        {
            RolePermissions.For(role).Should().BeSubsetOf(administrator);
            RolePermissions.For(role).Count.Should().BeLessThan(administrator.Count);
        }
    }

    [Fact]
    public void ReadOnly_IsASubsetOfBothTheAnalystAndTheOperator()
    {
        IReadOnlySet<Permission> readOnly = RolePermissions.For(UserRole.ReadOnly);

        readOnly.Should().BeSubsetOf(RolePermissions.For(UserRole.Analyst));
        readOnly.Should().BeSubsetOf(RolePermissions.For(UserRole.Operator));
    }
}
