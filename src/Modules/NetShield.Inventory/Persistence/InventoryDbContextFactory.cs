using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NetShield.Inventory.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time. The running application never uses it.
/// </summary>
/// <remarks>
/// No connection string is compiled in — SPEC.md §5 admits none, and generating a migration needs
/// no database at all. Applying one does, and the answer for a running system is
/// <c>NetShield.Web.Host --migrate</c> rather than this; this exists so that
/// <c>dotnet ef migrations add</c> has a context to reflect over.
/// <code>
/// NETSHIELD_MIGRATION_CONNECTION="..." dotnet ef database update \
///   --project src/Modules/NetShield.Inventory --startup-project src/Modules/NetShield.Inventory
/// </code>
/// </remarks>
public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    /// <summary>Where <c>dotnet ef database update</c> reads its connection string from.</summary>
    public const string ConnectionEnvironmentVariable = "NETSHIELD_MIGRATION_CONNECTION";

    public InventoryDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<InventoryDbContext> options = new();

        options.UseNpgsql(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable))
            .UseInventoryConventions();

        return new InventoryDbContext(options.Options);
    }
}
