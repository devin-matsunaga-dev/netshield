using FluentAssertions;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers the WP-0.3 criterion that an unhandled exception returns problem details with no
/// stack trace, in every environment (SPEC.md §5).
/// </summary>
public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task AnUnhandledException_Returns500ProblemDetails()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        ApiProbeHost.ProbeResponse response = await host.GetAsync("/boom", TestContext.Current.CancellationToken);

        response.Status.Should().Be(500);
        response.ContentType.Should().Be("application/problem+json");
        response.Member("title").Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task AnUnhandledException_CarriesATraceId_SoTheCallerCanQuoteIt()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        (await host.GetAsync("/boom", TestContext.Current.CancellationToken))
            .Member("traceId").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AnUnhandledException_LeaksNoStackTrace_NoTypeName_AndNoCredential()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        string body = (await host.GetAsync("/boom", TestContext.Current.CancellationToken)).Body;

        body.Should().NotContain(ApiProbeHost.LeakedSecret, "SPEC.md §5 admits no credential in an API response");
        body.Should().NotContain("InvalidOperationException").And.NotContain("npgsql");
        body.Should().NotContain("   at ").And.NotContain("StackTrace");
    }

    [Fact]
    public async Task AnUnhandledException_IsHandledInProductionToo()
    {
        // Development is where the developer exception page would otherwise render the trace,
        // and Production is where the handler must not quietly stop being registered.
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken, "Production");

        ApiProbeHost.ProbeResponse response = await host.GetAsync("/boom", TestContext.Current.CancellationToken);

        response.Status.Should().Be(500);
        response.ContentType.Should().Be("application/problem+json");
        response.Body.Should().NotContain(ApiProbeHost.LeakedSecret);
    }
}
