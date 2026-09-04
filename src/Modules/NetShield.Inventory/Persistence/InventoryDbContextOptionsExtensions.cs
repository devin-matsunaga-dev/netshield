using Microsoft.EntityFrameworkCore;

using NetShield.Platform.Persistence;

namespace NetShield.Inventory.Persistence;

/// <summary>
/// The conventions every <see cref="InventoryDbContext"/> is built with, wherever it is built.
/// </summary>
public static class InventoryDbContextOptionsExtensions
{
    /// <summary>
    /// Applies the platform's <c>snake_case</c> naming and this module's own migration history
    /// table.
    /// </summary>
    /// <remarks>
    /// Every path that builds the context has to call this — the running host, the migration
    /// step, the design-time factory, and any test — or a migration generated in one of them
    /// would be recorded in a table the others do not read.
    /// </remarks>
    public static DbContextOptionsBuilder UseInventoryConventions(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(InventoryDbContext.MigrationsHistoryTable))
            .UseNetShieldConventions();
    }
}
