using System.Globalization;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// The naming scheme that lets a permission be spelled as an authorization policy name.
/// </summary>
/// <remarks>
/// Policies are materialised on demand by <see cref="PermissionPolicyProvider"/> rather than
/// registered one by one at startup, so adding a member to <see cref="Permission"/> needs no
/// second edit somewhere a reviewer has to remember to look.
/// </remarks>
public static class PermissionPolicy
{
    /// <summary>What every generated policy name starts with.</summary>
    public const string Prefix = "netshield:permission:";

    /// <summary>The policy name that requires <paramref name="permission"/>.</summary>
    public static string NameFor(Permission permission) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{permission}");

    /// <summary>
    /// The permission a policy name asks for, or <see langword="null"/> when the name is not one
    /// of ours and belongs to the default provider.
    /// </summary>
    public static Permission? PermissionFor(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return Enum.TryParse(policyName[Prefix.Length..], ignoreCase: false, out Permission permission)
            ? permission
            : null;
    }
}
