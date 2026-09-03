using System.Xml.Linq;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The repository as these tests see it: the files on disk, not the assemblies the build
/// produced. MSBuild-level rules — a target framework, a package version, a project reference —
/// are not observable from compiled metadata, and a project nothing references yet has no
/// assembly to inspect at all.
/// </summary>
internal static class Repository
{
    /// <summary>The directory holding <c>NetShield.sln</c>.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>Every project in the repository, build output excluded.</summary>
    internal static IReadOnlyList<string> ProjectFiles { get; } =
        EnumerateFiles(Root, "*.csproj");

    /// <summary>Files matching <paramref name="pattern"/> under <paramref name="root"/>.</summary>
    internal static IReadOnlyList<string> EnumerateFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    /// <summary>A path as it would be written in a commit, for a readable assertion message.</summary>
    internal static string RelativeToRoot(string path) => Path.GetRelativePath(Root, path);

    internal static XDocument LoadProjectFile(string path)
    {
        File.Exists(path).Should().BeTrue($"{RelativeToRoot(path)} is required by CONVENTIONS.md §1");

        return XDocument.Load(path);
    }

    /// <summary>The projects a project references, by file name without the extension.</summary>
    internal static IReadOnlyList<string> ProjectReferencesOf(string projectFile) =>
        LoadProjectFile(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.Length > 0)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static bool IsBuildOutput(string path) =>
        Path.GetRelativePath(Root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules");

    private static string FindRoot()
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
