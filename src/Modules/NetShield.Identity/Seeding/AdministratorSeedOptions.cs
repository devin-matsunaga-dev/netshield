namespace NetShield.Identity.Seeding;

/// <summary>
/// The first-run administrator. Bound from <c>Identity:Seed</c>.
/// </summary>
/// <remarks>
/// The password is supplied by the environment — an Aspire parameter in development, a mounted
/// secret in deployment — and never has a default. A shipped default password is a shipped
/// vulnerability, and a generated one would have to be written somewhere for the operator to
/// read it, which SPEC.md §5 does not allow to be a log.
/// </remarks>
public sealed class AdministratorSeedOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Identity:Seed";

    /// <summary>The account name created on first run.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>The name shown in the header's user block.</summary>
    public string DisplayName { get; set; } = "Administrator";

    /// <summary>
    /// The initial password. Absent, no account is created and the seeder says so once.
    /// </summary>
    public string? Password { get; set; }
}
