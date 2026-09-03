namespace NetShield.Contracts.Identity;

/// <summary>
/// The body of <c>POST /api/v1/auth/password</c>. The current password is required even for a
/// forced first-run change, so that a stolen session cannot become a stolen account.
/// </summary>
/// <param name="CurrentPassword">The password the session was established with.</param>
/// <param name="NewPassword">The replacement, checked against the password policy.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
