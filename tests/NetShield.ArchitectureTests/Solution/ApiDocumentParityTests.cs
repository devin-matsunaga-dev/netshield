using System.Text.RegularExpressions;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Keeps the host that produces the committed OpenAPI document in step with the composition root.
/// </summary>
/// <remarks>
/// CONVENTIONS.md §4 generates the TypeScript client from the document, so a module whose
/// endpoints <c>NetShield.Web.Host</c> maps and the document host does not would be missing from
/// the client with nothing failing to say so. The document host cannot simply boot the
/// composition root — ARCHITECTURE.md §4 lets nothing reference <c>NetShield.Web.Host</c> — so
/// the two lists are compared as text instead.
/// </remarks>
public sealed partial class ApiDocumentParityTests
{
    private const string CompositionRoot = "src/NetShield.Web.Host/Program.cs";
    private const string DocumentHost = "tests/NetShield.UnitTests/Api/ApiDocument.cs";

    /// <summary>
    /// Endpoint groups that are deliberately absent from the document. The health endpoints are
    /// for a container probe and the Aspire dashboard; they are not under <c>/api</c> and no
    /// client should carry a method for them.
    /// </summary>
    private static readonly string[] NotDescribed = ["MapDefaultEndpoints"];

    [Fact]
    public void DocumentHost_MapsEveryEndpointGroup_TheCompositionRootMaps()
    {
        IReadOnlyCollection<string> mapped = EndpointGroupsIn(CompositionRoot);
        IReadOnlyCollection<string> described = EndpointGroupsIn(DocumentHost);

        mapped.Should().NotBeEmpty($"{CompositionRoot} maps at least one endpoint group");

        described.Should().BeEquivalentTo(
            mapped,
            $"{DocumentHost} produces the committed OpenAPI document and has to describe exactly "
            + $"what {CompositionRoot} serves");
    }

    private static IReadOnlyCollection<string> EndpointGroupsIn(string relativePath)
    {
        string source = File.ReadAllText(Path.Combine(Repository.Root, relativePath));

        return EndpointGroupCall()
            .Matches(source)
            .Select(match => match.Groups[1].Value)
            .Where(name => !NotDescribed.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A <c>Map{Module}Endpoints()</c> call — the single registration extension
    /// CONVENTIONS.md §2 gives every module.
    /// </summary>
    [GeneratedRegex(@"\.(Map\w*Endpoints)\(")]
    private static partial Regex EndpointGroupCall();
}
