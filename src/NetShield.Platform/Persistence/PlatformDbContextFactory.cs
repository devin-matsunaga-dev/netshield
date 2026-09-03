using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NetShield.Platform.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time. The running application never uses it.
/// </summary>
/// <remarks>
/// No connection string is compiled in — SPEC.md §5 admits none, and generating a migration
/// needs no database at all. Applying one does, so <c>dotnet ef database update</c> reads the
/// connection from <see cref="ConnectionEnvironmentVariable"/>:
/// <code>
/// NETSHIELD_MIGRATION_CONNECTION="..." dotnet ef database update \
///   --project src/NetShield.Platform --startup-project src/NetShield.Platform
/// </code>
/// </remarks>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    /// <summary>Where <c>dotnet ef database update</c> reads its connection string from.</summary>
    public const string ConnectionEnvironmentVariable = "NETSHIELD_MIGRATION_CONNECTION";

    public PlatformDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<PlatformDbContext> options = new();

        options.UseNpgsql(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable))
            .UseNetShieldConventions();

        return new PlatformDbContext(options.Options);
    }
}
