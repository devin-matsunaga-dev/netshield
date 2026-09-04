using System.Text.RegularExpressions;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Enforces ARCHITECTURE.md §8 and CLAUDE.md: <c>audit_log</c> is append-only, and no update or
/// delete path may ever be written for it, in any package, for any reason.
/// </summary>
/// <remarks>
/// <para>
/// Read as files, like every rule in this project. WP-0.3 settled that these tests take no
/// dependency on the code they judge; here that also means the rule holds over a method nobody
/// has called yet, which is exactly the state a mistake is in when it is cheapest to catch.
/// </para>
/// <para>
/// Comments are stripped before the source is judged, so that a file may say the words "delete"
/// and "remove" while explaining why it does neither.
/// </para>
/// </remarks>
public sealed partial class AuditLogAppendOnlyTests
{
    [Fact]
    public void NoFileTouchingTheAuditLog_DeclaresOrCallsAMutatingOperation()
    {
        IReadOnlyList<string> offenders = AuditSourceFiles
            .SelectMany(file => MutatingOperation()
                .Matches(file.Code)
                .Select(match => $"{Repository.RelativeToRoot(file.Path)}: {match.Value}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "no update or delete path may ever exist for audit_log — not a method, not a call, "
            + "not a DbSet handle that would offer one (ARCHITECTURE.md §8, CLAUDE.md)");
    }

    [Fact]
    public void ThePlatformContext_ExposesNoDbSetOverTheAuditLog()
    {
        string context = StripComments(File.ReadAllText(PlatformDbContextFile));

        context.Should().NotMatchRegex(
            @"DbSet\s*<\s*AuditEntry\s*>",
            "a DbSet is a handle with Remove, RemoveRange, ExecuteDelete and ExecuteUpdate "
            + "hanging off it; the audit log is reached with Set<AuditEntry>() and only added to");
    }

    [Fact]
    public void TheAuditEntry_HasNoSettableProperty()
    {
        string entry = StripComments(File.ReadAllText(AuditEntryFile));

        entry.Should().NotMatchRegex(
            @"\bset\s*;",
            "a row that can be assigned to after it is written is a row that can be rewritten; "
            + "every property is init-only");
    }

    [Fact]
    public void TheMigrationCreatingTheTable_AlsoInstallsTheDatabaseRule()
    {
        string migration = CreatingMigration;

        migration.Should().Contain(
            "CREATE TRIGGER audit_log_append_only",
            "the code-level guard is half of the rule; the other half has to hold when someone "
            + "opens psql (ARCHITECTURE.md §8)");

        migration.Should().Contain(
            "BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_log",
            "all three have to be refused — TRUNCATE bypasses row-level triggers and is the "
            + "fastest way to empty a table");

        migration.Should().Contain(
            "FOR EACH STATEMENT",
            "a statement matching no rows must fail too, or the rule has a hole in it");

        migration.Should().Contain(
            "RAISE EXCEPTION",
            "the write has to fail, not be silently discarded the way a DO INSTEAD NOTHING rule "
            + "would discard it");
    }

    /// <summary>
    /// Every source file that has anything to do with the audit log: the module that owns it,
    /// and anything anywhere in <c>src</c> that names the entity or the table.
    /// </summary>
    private static IReadOnlyList<SourceFile> AuditSourceFiles { get; } = Repository
        .EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs")
        .Where(path => !IsMigration(path))
        .Select(path => new SourceFile(path, Readable(File.ReadAllText(path))))
        .Where(file => file.Code.Contains("AuditEntry", StringComparison.Ordinal)
            || file.Code.Contains("audit_log", StringComparison.Ordinal))
        .ToList();

    private static string PlatformDbContextFile { get; } = Path.Combine(
        Repository.Root, "src", "NetShield.Platform", "Persistence", "PlatformDbContext.cs");

    private static string AuditEntryFile { get; } = Path.Combine(
        Repository.Root, "src", "NetShield.Platform", "Auditing", "AuditEntry.cs");

    /// <summary>
    /// The migration that creates <c>audit_log</c>. Found by content rather than by name, so
    /// that renaming it does not quietly retire this test.
    /// </summary>
    private static string CreatingMigration { get; } = Repository
        .EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs")
        .Where(IsMigration)
        .Select(File.ReadAllText)
        .Single(text => text.Contains("CreateTable(", StringComparison.Ordinal)
            && text.Contains("name: \"audit_log\"", StringComparison.Ordinal));

    /// <summary>
    /// Migrations are exempt from the mutating-operation scan. The one that creates the table has
    /// to be able to name <c>DROP TRIGGER</c> in its <c>Down</c>, and a migration cannot write a
    /// path anything calls at run time.
    /// </summary>
    private static bool IsMigration(string path) =>
        Path.GetRelativePath(Repository.Root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("Migrations");

    /// <summary>
    /// The file with its comments taken out, so that prose explaining the rule cannot break it.
    /// String literals are left alone — a SQL literal saying <c>DELETE</c> is exactly what this
    /// wants to catch.
    /// </summary>
    private static string StripComments(string source) => Comment().Replace(source, " ");

    /// <summary>
    /// The file as this rule reads it: comments gone, and the phrases that read as a mutation
    /// without being one taken out with them.
    /// </summary>
    private static string Readable(string source) =>
        Exemptions.Aggregate(
            StripComments(source),
            (text, exemption) => text.Replace(exemption, " ", StringComparison.Ordinal));

    /// <summary>
    /// The phrases that match the pattern and are not a mutation of anything, let alone of
    /// <c>audit_log</c>. Spelled out in full so that adding one has to be a deliberate edit here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HttpMethods.IsDelete</c> is how the audit middleware decides that a call changes state.
    /// It names no row and reaches no table.
    /// </para>
    /// <para>
    /// <c>DeletedAt</c> is the soft-delete column CONVENTIONS.md §3 puts on the inventory tables.
    /// Reading it is how a handler tells a live device from a removed one, and it cannot be a
    /// path into <c>audit_log</c>: <c>AuditEntry</c> has no such property and the table has no
    /// such column — WP-0.5 gave it neither that nor an <c>updated_at</c>, on purpose.
    /// </para>
    /// <para>
    /// A property rather than a field: <c>AuditSourceFiles</c> is initialised above and would
    /// otherwise read this before the field initialiser had run.
    /// </para>
    /// </remarks>
    private static string[] Exemptions => ["HttpMethods.IsDelete", "DeletedAt"];

    private sealed record SourceFile(string Path, string Code);

    /// <summary>A line comment, a block comment or an XML documentation comment.</summary>
    [GeneratedRegex(@"//[^\r\n]*|/\*[\s\S]*?\*/", RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    /// <summary>
    /// Anything that reads as changing or discarding a row. Matched as an identifier fragment, so
    /// it catches a declared <c>DeleteAsync</c> and a called <c>ExecuteDeleteAsync</c> alike.
    /// </summary>
    [GeneratedRegex(
        @"\b\w*(?:Delete|Remove|Purge|Truncate|Erase|ExecuteUpdate)\w*\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex MutatingOperation();
}
