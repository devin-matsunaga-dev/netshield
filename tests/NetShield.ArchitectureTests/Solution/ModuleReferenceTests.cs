using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Enforces the module rules in ARCHITECTURE.md §4. A modular monolith is only modular for as
/// long as something refuses the reference that would end it, and the reference is refused here,
/// at the project graph, where it is declared.
/// </summary>
/// <remarks>
/// These rules are checked against <c>ProjectReference</c> rather than against loaded assemblies
/// on purpose. A reference a project declares but does not yet use is elided from assembly
/// metadata entirely, so a reflection-based rule would pass over the ten module projects that
/// are still empty — reporting confidence it does not have. An assembly-level rule library earns
/// its place in WP-1.1, alongside the first module that holds real types.
/// </remarks>
public sealed class ModuleReferenceTests
{
    private const string Contracts = "NetShield.Contracts";
    private const string Platform = "NetShield.Platform";
    private const string WebHost = "NetShield.Web.Host";

    /// <summary>
    /// Aspire's orchestrator. It references the projects it launches, which is how a resource is
    /// declared, and it is dev-time only and never deployed (ARCHITECTURE.md §2) — so it sits
    /// outside the runtime dependency graph these rules describe.
    /// </summary>
    private const string AppHost = "NetShield.AppHost";

    [Fact]
    public void Modules_AreTheTenProjects_ArchitectureNames()
    {
        ModuleProjects.Select(Repository.RelativeToRoot)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Should().BeEquivalentTo(
                "NetShield.Inventory",
                "NetShield.Telemetry",
                "NetShield.Flows",
                "NetShield.Logs",
                "NetShield.Alerting",
                "NetShield.Configs",
                "NetShield.Compliance",
                "NetShield.Vulnerabilities",
                "NetShield.Reporting",
                "NetShield.Identity");
    }

    [Fact]
    public void NoModule_ReferencesAnotherModule()
    {
        IReadOnlyList<string> offenders = ModuleProjects
            .SelectMany(project => Repository.ProjectReferencesOf(project)
                .Where(reference => ModuleNames.Contains(reference))
                .Select(reference => $"{Repository.RelativeToRoot(project)} -> {reference}"))
            .ToList();

        offenders.Should().BeEmpty(
            "ARCHITECTURE.md §4: cross-module communication is asynchronous, through the bus, "
            + "carrying Contracts types — never a direct reference");
    }

    [Fact]
    public void AModule_ReferencesOnlyContractsAndPlatform()
    {
        IReadOnlyList<string> offenders = ModuleProjects
            .SelectMany(project => Repository.ProjectReferencesOf(project)
                .Where(reference => reference is not (Contracts or Platform))
                .Select(reference => $"{Repository.RelativeToRoot(project)} -> {reference}"))
            .ToList();

        offenders.Should().BeEmpty("ARCHITECTURE.md §4 allows a module exactly two references");
    }

    [Fact]
    public void NothingReferences_WebHost()
    {
        IReadOnlyList<string> offenders = Repository.ProjectFiles
            .Where(project => Path.GetFileNameWithoutExtension(project) != AppHost)
            .Where(project => Repository.ProjectReferencesOf(project).Contains(WebHost))
            .Select(Repository.RelativeToRoot)
            .ToList();

        offenders.Should().BeEmpty(
            "ARCHITECTURE.md §4: Web.Host is the composition root, and a composition root that "
            + "something else composes is not one");
    }

    [Fact]
    public void Contracts_ReferencesNothing()
    {
        Repository.ProjectReferencesOf(ProjectFor(Contracts))
            .Should().BeEmpty("ARCHITECTURE.md §4 describes Contracts as having no dependencies");
    }

    [Fact]
    public void Platform_ReferencesOnlyContracts()
    {
        Repository.ProjectReferencesOf(ProjectFor(Platform))
            .Should().BeEquivalentTo(
                [Contracts],
                "Platform is cross-cutting: everything may depend on it, so it may depend on almost nothing");
    }

    [Fact]
    public void NoModule_IsReferencedByAnythingButWebHost()
    {
        IReadOnlyList<string> offenders = Repository.ProjectFiles
            .Where(project => Path.GetFileNameWithoutExtension(project) is not (WebHost or AppHost))
            .Where(project => !IsTestProject(project))
            .SelectMany(project => Repository.ProjectReferencesOf(project)
                .Where(reference => ModuleNames.Contains(reference))
                .Select(reference => $"{Repository.RelativeToRoot(project)} -> {reference}"))
            .ToList();

        offenders.Should().BeEmpty("a module is reached through the composition root or through the bus");
    }

    private static IReadOnlyList<string> ModuleProjects { get; } = Repository.ProjectFiles
        .Where(path => Repository.RelativeToRoot(path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("Modules"))
        .ToList();

    private static HashSet<string> ModuleNames { get; } = ModuleProjects
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => name is not null)
        .Select(name => name!)
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsTestProject(string path) =>
        Repository.RelativeToRoot(path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("tests");

    private static string ProjectFor(string projectName) =>
        Repository.ProjectFiles.Single(path => Path.GetFileNameWithoutExtension(path) == projectName);
}
