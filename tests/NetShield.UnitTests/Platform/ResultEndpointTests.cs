using FluentAssertions;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers the translation from a handler's result to an HTTP response (CONVENTIONS.md §4).
/// The handler never names a status code; this is the only place one is chosen.
/// </summary>
public sealed class ResultEndpointTests
{
    [Fact]
    public async Task ASuccessfulResultWithAValue_Returns200()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        ApiProbeHost.ProbeResponse response = await host.GetAsync("/ok", TestContext.Current.CancellationToken);

        response.Status.Should().Be(200);
        response.Member("hostname").Should().Be("core-sw-01");
    }

    [Fact]
    public async Task ASuccessfulResultWithNoValue_Returns204()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        (await host.GetAsync("/no-content", TestContext.Current.CancellationToken)).Status.Should().Be(204);
    }

    [Fact]
    public async Task ACreatingResult_Returns201WithALocation()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        ApiProbeHost.ProbeResponse response = await host.GetAsync("/created", TestContext.Current.CancellationToken);

        response.Status.Should().Be(201);
        response.Location.Should().Be("/api/v1/devices/core-sw-01", "CONVENTIONS.md §4 requires the header on a create");
    }

    [Theory]
    [InlineData("/validation", 400)]
    [InlineData("/not-found", 404)]
    [InlineData("/forbidden", 403)]
    [InlineData("/conflict", 409)]
    [InlineData("/unprocessable", 422)]
    [InlineData("/rate-limited", 429)]
    public async Task AFailedResult_ReturnsTheStatusCodeItsKindMapsTo(string path, int expected)
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        (await host.GetAsync(path, TestContext.Current.CancellationToken)).Status.Should().Be(expected);
    }

    [Fact]
    public async Task AFailedResult_IsProblemDetailsCarryingATraceId()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        ApiProbeHost.ProbeResponse response = await host.GetAsync("/conflict", TestContext.Current.CancellationToken);

        response.ContentType.Should().Be("application/problem+json");
        response.Member("traceId").Should().NotBeNullOrWhiteSpace("CONVENTIONS.md §4 requires one in every problem response");
        response.Member("status").Should().Be("409");
        response.Member("type").Should().StartWith("https://");
        response.Member("detail").Should().Be("That address is already in use.");
    }

    [Fact]
    public async Task AFailedResult_CarriesTheErrorCode_SoAClientNeverParsesTheMessage()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        (await host.GetAsync("/conflict", TestContext.Current.CancellationToken))
            .Member("code").Should().Be("device.duplicate-ip");
    }

    [Fact]
    public async Task AValidationFailure_CarriesThePerFieldErrors()
    {
        await using ApiProbeHost host = await ApiProbeHost.StartAsync(TestContext.Current.CancellationToken);

        ApiProbeHost.ProbeResponse response = await host.GetAsync("/validation", TestContext.Current.CancellationToken);

        response.Status.Should().Be(400);
        response.Json.GetProperty("errors").GetProperty("hostname")[0].GetString().Should().Be("Required.");
    }
}
