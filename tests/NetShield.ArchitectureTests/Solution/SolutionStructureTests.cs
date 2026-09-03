using System.Xml.Linq;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Enforces the repository-wide MSBuild invariants from CONVENTIONS.md §2 and
/// ARCHITECTURE.md §10. Checked against the project files on disk — see
/// <see cref="Repository"/> for why — so a newly added project is covered the moment
/// it exists, whether or not anything references it yet.
/// </summary>
public sealed class SolutionStructureTests
{
    private const string TargetFramework = "net10.0";

    [Fact]
    public void DirectoryBuildProps_SetsTheSharedProperties_ForEveryProject()
    {
        XElement properties = Repository.LoadProjectFile(Path.Combine(Repository.Root, "Directory.Build.props"))
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
        Repository.LoadProjectFile(Path.Combine(Repository.Root, "Directory.Packages.props"))
            .Descendants("ManagePackageVersionsCentrally")
            .Should().ContainSingle().Which.Value.Should().Be("true");
    }

    [Fact]
    public void EveryProject_DeclaresNoTargetFramework_SoNoneCanDivergeFromNet10()
    {
        IReadOnlyList<string> offenders = Repository.ProjectFiles
            .Where(path => Repository.LoadProjectFile(path)
                .Descendants()
                .Any(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks"))
            .Select(Repository.RelativeToRoot)
            .ToList();

        offenders.Should().BeEmpty(
            "CONVENTIONS.md §2 sets the target framework in Directory.Build.props and never per-project");
    }

    [Fact]
    public void EveryPackageReference_OmitsItsVersion_SoVersionsStayCentral()
    {
        IReadOnlyList<string> offenders = Repository.ProjectFiles
            .SelectMany(path => Repository.LoadProjectFile(path)
                .Descendants("PackageReference")
                .Where(reference => reference.Attribute("Version") is not null)
                .Select(reference => $"{Repository.RelativeToRoot(path)}: {reference.Attribute("Include")?.Value}"))
            .ToList();

        offenders.Should().BeEmpty(
            "CONVENTIONS.md §2 treats a versioned PackageReference in a .csproj as a bug");
    }

    [Fact]
    public void EveryPackageReference_HasACentralVersion_SoRestoreIsDeterministic()
    {
        HashSet<string> pinned = Repository.LoadProjectFile(Path.Combine(Repository.Root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(version => version.Attribute("Include")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> unpinned = Repository.ProjectFiles
            .SelectMany(path => Repository.LoadProjectFile(path)
                .Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty))
            .Where(package => package.Length > 0 && !pinned.Contains(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        unpinned.Should().BeEmpty("every referenced package needs a PackageVersion in Directory.Packages.props");
    }
}
