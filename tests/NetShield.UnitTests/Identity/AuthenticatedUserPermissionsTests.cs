using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Identity.Authentication;
using NetShield.Identity.Users;

using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// The permission list an <see cref="AuthenticatedUser"/> carries is what the SPA draws its
/// navigation and its write controls from (WP-0.7). It is presentation only — every protected
/// request re-resolves the same table server-side — but a list that disagreed with the table
/// would show a user a control the API then refuses, which is worse than hiding it.
/// </summary>
public sealed class AuthenticatedUserPermissionsTests
{
    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public void ToAuthenticatedUser_CarriesExactlyWhatTheRoleTableGrants(UserRole role)
    {
        AuthenticatedUser described = SessionService.ToAuthenticatedUser(UserWith(role));

        described.Permissions.Should().BeEquivalentTo(RolePermissions.For(role));
    }

    [Fact]
    public void ToAuthenticatedUser_ForAReadOnlySession_CarriesNoWritePermission()
    {
        AuthenticatedUser described = SessionService.ToAuthenticatedUser(UserWith(UserRole.ReadOnly));

        described.Permissions.Should().NotContain(
        [
            Permission.InventoryWrite,
            Permission.CredentialsManage,
            Permission.PoliciesWrite,
            Permission.AuditRead,
            Permission.SystemAdminister
        ]);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Analyst)]
    [InlineData(UserRole.ReadOnly)]
    public void ToAuthenticatedUser_OrdersPermissionsTheSameWayEveryTime(UserRole role)
    {
        // Two responses for the same role have to be byte-identical: the set behind the table
        // makes no promise about enumeration order, and a list that reordered between calls
        // would churn every cache and every snapshot that holds it.
        IReadOnlyList<Permission> first = SessionService.ToAuthenticatedUser(UserWith(role)).Permissions;
        IReadOnlyList<Permission> second = SessionService.ToAuthenticatedUser(UserWith(role)).Permissions;

        first.Should().ContainInOrder(second).And.HaveSameCount(second);
        first.Should().BeInAscendingOrder();
    }

    [Fact]
    public void ToAuthenticatedUser_CarriesNoHashAndNoLockoutState()
    {
        // The shape is the whole of what the client is told about an account (SPEC.md §5). This
        // fails the moment somebody widens it, which is the point.
        string[] members = [.. typeof(AuthenticatedUser).GetProperties().Select(property => property.Name)];

        members.Should().BeEquivalentTo(
            nameof(AuthenticatedUser.Id),
            nameof(AuthenticatedUser.Username),
            nameof(AuthenticatedUser.DisplayName),
            nameof(AuthenticatedUser.Role),
            nameof(AuthenticatedUser.MustChangePassword),
            nameof(AuthenticatedUser.Permissions));
    }

    private static User UserWith(UserRole role) => new()
    {
        Id = Guid.CreateVersion7(),
        Username = "kim",
        NormalizedUsername = "KIM",
        DisplayName = "Kim Rivera",
        PasswordHash = "not-read-here",
        Role = role,
        IsActive = true,
        MustChangePassword = false,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };
}
