using Microsoft.AspNetCore.Authorization;

namespace NetShield.Platform.Authorization;

/// <summary>
/// Requires that the session does not still owe a password change.
/// </summary>
/// <remarks>
/// WP-0.4 set <c>must_change_password</c>, returned it, and cleared it, but nothing refused a
/// request while it stood — the refusal belongs with the authorization pipeline, and this is it.
/// An endpoint that a user in that state must still be able to reach declares
/// <see cref="AllowPendingPasswordChangeAttribute"/>.
/// </remarks>
public sealed class PasswordChangeNotPendingRequirement : IAuthorizationRequirement;
