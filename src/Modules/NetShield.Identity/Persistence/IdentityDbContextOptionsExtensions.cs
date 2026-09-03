using Microsoft.EntityFrameworkCore;

using NetShield.Platform.Persistence;

namespace NetShield.Identity.Persistence;

/// <summary>
/// The conventions every <see cref="IdentityDbContext"/> is built with, wherever it is built.
/// </summary>
public static class IdentityDbContextOptionsExtensions
{
    /// <summary>
    /// Applies the platform's <c>snake_case</c> naming and this module's own migration history
    /// table.
    /// </summary>
    /// <remarks>
    /// Every path that builds the context has to call this — the running host, the design-time
    /// factory, and any test — or a migration generated in one of them would be recorded in a
    /// table the others do not read.
    /// </remarks>
    public static DbContextOptionsBuilder UseIdentityConventions(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTable))
            .UseNetShieldConventions();
    }
}
