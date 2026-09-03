using Microsoft.EntityFrameworkCore;

namespace NetShield.Platform.Persistence;

/// <summary>
/// The database conventions every NetShield <c>DbContext</c> is built with.
/// </summary>
public static class DbContextOptionsExtensions
{
    /// <summary>
    /// Applies CONVENTIONS.md §3 naming: <c>snake_case</c> tables and columns. Applied here
    /// rather than spelled out per property, because a convention that has to be remembered on
    /// every entity is one that will eventually be forgotten on one.
    /// </summary>
    /// <remarks>
    /// Every path that builds a context has to call this — the running host, the design-time
    /// factory that generates migrations, and any test — or the migration and the runtime model
    /// would disagree about the name of every column.
    /// </remarks>
    public static DbContextOptionsBuilder UseNetShieldConventions(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseSnakeCaseNamingConvention();
    }
}
