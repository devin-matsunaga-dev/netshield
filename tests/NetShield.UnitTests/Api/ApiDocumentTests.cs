using FluentAssertions;

namespace NetShield.UnitTests.Api;

/// <summary>
/// Keeps the committed OpenAPI document honest. CONVENTIONS.md §4: the document is generated
/// from the endpoints, the TypeScript client is generated from the document, and a drifted
/// client is a failing build. This is the first of the two gates that make that true; the
/// second lives in the SPA, where the generated types are checked against this file.
/// </summary>
public sealed class ApiDocumentTests
{
    [Fact]
    public async Task CommittedDocument_MatchesTheOneTheApiDescribesItselfWith()
    {
        string generated = await ApiDocument.GenerateAsync(TestContext.Current.CancellationToken);
        string path = ApiDocument.CommittedFile;

        if (Environment.GetEnvironmentVariable(ApiDocument.UpdateVariable) is { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated, TestContext.Current.CancellationToken);

            return;
        }

        File.Exists(path).Should().BeTrue($"{ApiDocument.CommittedPath} is what the SPA client is generated from");

        string committed = ApiDocument.Normalize(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        committed.Should().Be(
            generated,
            "the API changed and {0} was not regenerated — run the suite with {1}=1 and commit the result",
            ApiDocument.CommittedPath,
            ApiDocument.UpdateVariable);
    }

    [Fact]
    public async Task Document_DescribesOnlyApiPaths_SoNoClientMethodIsGeneratedForAProbe()
    {
        string generated = await ApiDocument.GenerateAsync(TestContext.Current.CancellationToken);

        generated.Should().NotContain(
            "\"/health",
            "the health endpoints are for a container probe and the Aspire dashboard, not for a client");
    }
}
