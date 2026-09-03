namespace NetShield.Platform.Authorization;

/// <summary>
/// Endpoint metadata exempting a route from <see cref="PasswordChangeNotPendingRequirement"/>.
/// </summary>
/// <remarks>
/// The exemption list is short by design and each entry earns itself: a user who must change
/// their password has to be able to see who they are, change it, and sign out. Anything else is
/// refused until they have.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowPendingPasswordChangeAttribute : Attribute;
