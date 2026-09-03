using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;

using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

using Npgsql;

namespace NetShield.Identity.Seeding;

/// <summary>
/// Creates the administrator a fresh installation is signed into for the first time, and does
/// nothing at all on every start after that.
/// </summary>
/// <remarks>
/// <para>
/// The account is created only when the table holds no users. That is the definition of first
/// run: an installation whose administrator was deleted has a recovery problem, not a seeding
/// one, and silently recreating the account would be a way back in that nobody authorised.
/// </para>
/// <para>
/// <see cref="User.MustChangePassword"/> is set, because the password came from configuration and
/// configuration is read by more people than the administrator.
/// </para>
/// <para>
/// A missing <c>users</c> table is reported and survived rather than thrown. Nothing applies
/// migrations at run time yet (see STATUS.md), and a seeding step is not a good enough reason to
/// take the whole API down — the outbox dispatcher degrades the same way.
/// </para>
/// </remarks>
internal sealed class FirstRunAdministratorSeeder(
    IServiceScopeFactory scopes,
    IOptions<AdministratorSeedOptions> options,
    ILogger<FirstRunAdministratorSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AdministratorSeedOptions seed = options.Value;

        if (string.IsNullOrEmpty(seed.Password))
        {
            logger.LogWarning(
                "No first-run administrator was created: {SectionName}:Password is not configured.",
                AdministratorSeedOptions.SectionName);

            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        try
        {
            if (await database.Users.AnyAsync(cancellationToken))
            {
                return;
            }
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogError(
                "No first-run administrator was created: the {TableName} table does not exist. "
                + "Apply the Identity migrations before signing in.",
                UserConfiguration.TableName);

            return;
        }

        PasswordPolicy policy = scope.ServiceProvider.GetRequiredService<PasswordPolicy>();
        Result acceptable = policy.Check(seed.Password, seed.Username, email: null);

        if (!acceptable.IsSuccess)
        {
            // Refused rather than accepted-with-a-warning: the one account that can administer
            // the system must not be the one account exempt from the password policy.
            logger.LogError(
                "No first-run administrator was created: the configured password does not meet the password policy.");

            return;
        }

        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();

        DateTimeOffset now = clock.UtcNow;

        database.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            Username = seed.Username,
            NormalizedUsername = UserName.Normalize(seed.Username),
            DisplayName = seed.DisplayName,
            PasswordHash = await hasher.HashAsync(seed.Password, cancellationToken),
            Role = UserRole.Administrator,
            MustChangePassword = true,
            IsActive = true,
            PasswordChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created the first-run administrator {Username}; it must change its password before anything else.",
            seed.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
