using System.Text.Json;
using System.Text.RegularExpressions;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Enforces SPEC.md §5 and ARCHITECTURE.md §8 at the repository level: the address of a store
/// and the credential that opens it reach a service from the orchestrator at run time, and are
/// never written into a configuration file or a source literal.
/// </summary>
public sealed partial class ConfigurationTests
{
    [Fact]
    public void NoSettingsFile_DeclaresAConnectionString()
    {
        IReadOnlyList<string> offenders = SettingsFiles
            .Where(DeclaresAConnectionString)
            .Select(RelativeToRoot)
            .ToList();

        offenders.Should().BeEmpty(
            "every connection string originates in NetShield.AppHost and is supplied at run time");
    }

    [Fact]
    public void NoSourceFile_HardcodesAHostOrACredential()
    {
        IReadOnlyList<string> offenders = SourceFiles
            .SelectMany(path => StringLiteral()
                .Matches(File.ReadAllText(path))
                .Where(literal => ConnectionShape().IsMatch(literal.Value))
                .Select(literal => $"{RelativeToRoot(path)}: {literal.Value}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a host, a port or a credential in source is a connection string that outlived its "
            + "environment; Aspire supplies all three (SPEC.md §5)");
    }

    private static bool DeclaresAConnectionString(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("ConnectionStrings", out JsonElement section)
            && section.EnumerateObject().Any();
    }

    /// <summary>A single-line C# string literal, interpolated or not, with escapes honoured.</summary>
    [GeneratedRegex(""""
        "(?:[^"\\\r\n]|\\.)*"
        """")]
    private static partial Regex StringLiteral();

    /// <summary>
    /// The shapes a connection string takes: an ADO.NET keyword, a store URI scheme, or a loopback
    /// address. Interpolation holes are deliberately not excluded — a literal that needs one of
    /// these keywords is assembling a connection string by hand either way.
    /// </summary>
    [GeneratedRegex(
        """(Host|Server|Data Source|User ID|User Id|Username|Password|Pwd)\s*=|(postgres|postgresql|redis|amqp)://|localhost|127\.0\.0\.1""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionShape();

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static IReadOnlyList<string> SettingsFiles { get; } =
        EnumerateFiles(RepositoryRoot, "appsettings*.json");

    private static IReadOnlyList<string> SourceFiles { get; } =
        EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs");

    private static IReadOnlyList<string> EnumerateFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    private static bool IsBuildOutput(string path) =>
        Path.GetRelativePath(RepositoryRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules");

    private static string RelativeToRoot(string path) => Path.GetRelativePath(RepositoryRoot, path);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NetShield.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate NetShield.sln above the test assembly.");
    }
}
