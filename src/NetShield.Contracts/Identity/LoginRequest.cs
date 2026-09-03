namespace NetShield.Contracts.Identity;

/// <summary>
/// Credentials presented to <c>POST /api/v1/auth/login</c>.
/// </summary>
/// <param name="Username">The account name, matched case-insensitively.</param>
/// <param name="Password">
/// The plaintext password. It exists in process memory for the length of one request, is never
/// stored, and never reaches a sink — <c>Password</c> is a redacted property name
/// (ARCHITECTURE.md §8).
/// </param>
public sealed record LoginRequest(string Username, string Password);
