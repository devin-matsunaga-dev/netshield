using Microsoft.EntityFrameworkCore;

using NetShield.Identity.Authentication;
using NetShield.Identity.Users;

namespace NetShield.Identity.Persistence;

/// <summary>
/// The Identity module's own tables — <c>users</c> and <c>refresh_tokens</c> — in the one
/// NetShield database (ARCHITECTURE.md §3).
/// </summary>
/// <remarks>
/// It keeps its own migration history table. Two contexts sharing EF's default would each be
/// reading rows written by the other's migrations, which is coupling with nothing to gain from
/// it: a module has to be able to say what it has applied without consulting the platform.
/// </remarks>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>The name this context records its applied migrations under.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history_identity";

    /// <summary>Local accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Issued refresh tokens, live and spent alike.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
