using NetShield.Platform.Results;

namespace NetShield.Identity.Authentication;

/// <summary>
/// The failures the authentication endpoints return.
/// </summary>
/// <remarks>
/// <see cref="InvalidCredentials"/> is one value on purpose. A wrong password, an unknown
/// username, a disabled account and a locked-out account all produce it, byte for byte, because
/// any difference between them tells an attacker which usernames are real and which accounts are
/// worth waiting for. The distinction is recorded in the log, where the operator can see it and
/// the caller cannot.
/// </remarks>
internal static class AuthenticationErrors
{
    /// <summary>The single answer to every failed sign-in and every rejected refresh.</summary>
    internal static Error InvalidCredentials { get; } = Error.Unauthenticated(
        "identity.invalid-credentials",
        "The username or password is incorrect.");

    /// <summary>There is no session to act on.</summary>
    internal static Error NoSession { get; } = Error.Unauthenticated(
        "identity.no-session",
        "You are not signed in.");

    /// <summary>
    /// The current password supplied with a change did not match. Deliberately not a 401: the
    /// session is perfectly valid, and answering 401 would make a typo look like an expired
    /// session and sign the user out.
    /// </summary>
    internal static Error CurrentPasswordInvalid { get; } = Error.Unprocessable(
        "identity.current-password-invalid",
        "The current password is not correct.");

    /// <summary>The new password is the one already in use.</summary>
    internal static Error PasswordUnchanged { get; } = Error.Unprocessable(
        "identity.password-unchanged",
        "The new password must be different from the current one.");
}
