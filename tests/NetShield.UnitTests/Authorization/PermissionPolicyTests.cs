using FluentAssertions;

using NetShield.Contracts.Identity;

using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Authorization;

/// <summary>Covers the naming scheme that lets a permission be spelled as a policy name.</summary>
public sealed class PermissionPolicyTests
{
    [Fact]
    public void NameFor_ThenPermissionFor_RoundTripsEveryPermission()
    {
        foreach (Permission permission in Enum.GetValues<Permission>())
        {
            PermissionPolicy.PermissionFor(PermissionPolicy.NameFor(permission)).Should().Be(permission);
        }
    }

    [Theory]
    [InlineData("CookiePolicy")]
    [InlineData("")]
    public void PermissionFor_APolicyThatIsNotOurs_ReturnsNull(string policyName) =>
        PermissionPolicy.PermissionFor(policyName).Should().BeNull();

    [Fact]
    public void PermissionFor_OurPrefixWithANameThatIsNotAPermission_ReturnsNull() =>
        // Not an exception: an unrecognised name has to fall through to the default provider,
        // which is the thing that decides a policy nobody registered is a startup failure.
        PermissionPolicy.PermissionFor($"{PermissionPolicy.Prefix}TakeOverTheWorld").Should().BeNull();

    [Fact]
    public void PermissionFor_IsCaseSensitive() =>
        PermissionPolicy.PermissionFor($"{PermissionPolicy.Prefix}inventorywrite").Should().BeNull();
}
