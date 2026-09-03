using System.Xml.Linq;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Enforces the repository-wide MSBuild invariants from CONVENTIONS.md §2 and
/// ARCHITECTURE.md §10. These are checked against the project files on disk rather
/// than against loaded assemblies, so a newly added project is covered the moment
/// it exists, whether or not anything references it yet.
/// </summary>
public sealed class SolutionStructureTests
{
    private const string TargetFramework = "net10.0";

    [Fact]
    public void DirectoryBuildProps_SetsTheSharedProperties_ForEveryProject()
    {
        XElement properties = LoadProjectFile(Path.Combine(RepositoryRoot, "Directory.Build.props"))
            .Descendants("PropertyGroup")
            .Should().ContainSingle("the shared properties belong in one group").Subject;

        properties.Element("TargetFramework")?.Value.Should().Be(TargetFramework);
        properties.Element("Nullable")?.Value.Should().Be("enable");
        properties.Element("ImplicitUsings")?.Value.Should().Be("enable");
        properties.Element("TreatWarningsAsErrors")?.Value.Should().Be("true");
    }

    [Fact]
    public void DirectoryPackagesProps_EnablesCentralPackageManagement()
    {
        LoadProjectFile(Path.Combine(RepositoryRoot, "Directory.Packages.props"))
            .Descendants("ManagePackageVersionsCentrally")
            .Should().ContainSingle().Which.Value.Should().Be("true");
    }

    [Fact]
    public void EveryProject_DeclaresNoTargetFramework_SoNoneCanDivergeFromNet10()
    {
        IReadOnlyList<string> offenders = ProjectFiles
            .Where(path => LoadProjectFile(path)
                .Descendants()
                .Any(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks"))
            .Select(RelativeToRoot)
            .ToList();

        offenders.Should().BeEmpty(
            "CONVENTIONS.md §2 sets the target framework in Directory.Build.props and never per-project");
    }

    [Fact]
    public void EveryPackageReference_OmitsItsVersion_SoVersionsStayCentral()
    {
        IReadOnlyList<string> offenders = ProjectFiles
            .SelectMany(path => LoadProjectFile(path)
                .Descendants("PackageReference")
                .Where(reference => reference.Attribute("Version") is not null)
                .Select(reference => $"{RelativeToRoot(path)}: {reference.Attribute("Include")?.Value}"))
            .ToList();

        offenders.Should().BeEmpty(
            "CONVENTIONS.md §2 treats a versioned PackageReference in a .csproj as a bug");
    }

    [Fact]
    public void EveryPackageReference_HasACentralVersion_SoRestoreIsDeterministic()
    {
        HashSet<string> pinned = LoadProjectFile(Path.Combine(RepositoryRoot, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(version => version.Attribute("Include")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> unpinned = ProjectFiles
            .SelectMany(path => LoadProjectFile(path)
                .Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty))
            .Where(package => package.Length > 0 && !pinned.Contains(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        unpinned.Should().BeEmpty("every referenced package needs a PackageVersion in Directory.Packages.props");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static IReadOnlyList<string> ProjectFiles { get; } =
        Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    private static bool IsBuildOutput(string path)
    {
        string relative = Path.GetRelativePath(RepositoryRoot, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
    }

    private static string RelativeToRoot(string path) => Path.GetRelativePath(RepositoryRoot, path);

    private static XDocument LoadProjectFile(string path)
    {
        File.Exists(path).Should().BeTrue($"{RelativeToRoot(path)} is required by CONVENTIONS.md §1");
        return XDocument.Load(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NetShield.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate NetShield.sln above the test assembly.");
        }

        return directory.FullName;
    }
}
