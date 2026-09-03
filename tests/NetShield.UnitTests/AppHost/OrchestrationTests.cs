using Aspire.Hosting.ApplicationModel;

using FluentAssertions;

namespace NetShield.UnitTests.AppHost;

/// <summary>
/// Asserts the shape of the development orchestration declared by <c>NetShield.AppHost</c>:
/// the stores from ARCHITECTURE.md §3, their persistence, and the wiring that carries their
/// connection strings into the API.
/// </summary>
public sealed class OrchestrationTests(AppHostModel model) : IClassFixture<AppHostModel>
{
    /// <summary>The container resources that hold state and therefore need a volume.</summary>
    public static TheoryData<string> StatefulResources => ["postgres", "cache", "mailpit"];

    [Fact]
    public void Postgres_RunsTheTimescaleImage_SoHypertablesAreAvailable()
    {
        ContainerImageAnnotation image = ImageOf("postgres");

        image.Image.Should().Be("timescale/timescaledb",
            "ARCHITECTURE.md §3 stores metrics, flows and log events as Timescale hypertables");
        image.Tag.Should().StartWith("2.").And.EndWith("-pg17",
            "ARCHITECTURE.md §10 pins the version floor at PostgreSQL 17");
    }

    [Fact]
    public async Task Postgres_DisablesTimescaleTelemetry_SoNothingReportsHomeAtRuntime()
    {
        IReadOnlyDictionary<string, string> environment =
            await AppHostModel.EnvironmentOf(Container("postgres"));

        environment.Should().Contain("TIMESCALEDB_TELEMETRY", "off",
            "ARCHITECTURE.md §1 permits no outbound internet call at runtime and the image "
            + "reports usage on a schedule unless this is set");
    }

    [Theory]
    [MemberData(nameof(StatefulResources))]
    public void EveryStatefulResource_MountsANamedVolume_SoItSurvivesARestart(string name)
    {
        IReadOnlyList<ContainerMountAnnotation> mounts = Container(name)
            .Annotations.OfType<ContainerMountAnnotation>()
            .Where(mount => mount.Type == ContainerMountType.Volume)
            .ToList();

        mounts.Should().ContainSingle().Which.Source.Should().NotBeNullOrWhiteSpace();
        mounts.Single().IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void WebHost_ReferencesEveryStore_SoNoConnectionStringIsWrittenByHand()
    {
        IReadOnlyList<string> references = RelationshipsOf("web-host", "Reference");

        references.Should().BeEquivalentTo(["netshield", "cache", "mail", "identity-admin-password"],
            "the API reads PostgreSQL, Redis and the SMTP sink, Aspire supplies all three, and the "
            + "first-run administrator's password reaches it the same way rather than from a file");
    }

    [Fact]
    public void TheAdministratorSeedPassword_IsASecretParameter_SoItIsNeverWrittenIntoTheRepository()
    {
        ParameterResource parameter = model.Resource("identity-admin-password")
            .Should().BeAssignableTo<ParameterResource>().Subject;

        parameter.Secret.Should().BeTrue(
            "the dashboard must mask it and it must not be written to the manifest in plaintext");
    }

    [Fact]
    public void WebHost_WaitsForEveryStore_SoItNeverStartsAgainstAnUnreadyDependency()
    {
        IReadOnlyList<string> waits = model.Resource("web-host")
            .Annotations.OfType<WaitAnnotation>()
            .Select(wait => wait.Resource.Name)
            .ToList();

        waits.Should().BeEquivalentTo(["postgres", "netshield", "cache", "mailpit"]);
    }

    [Fact]
    public void WebHost_ReportsItsHealthFromTheReadinessEndpoint()
    {
        IReadOnlyList<string> keys = model.Resource("web-host")
            .Annotations.OfType<HealthCheckAnnotation>()
            .Select(check => check.Key)
            .ToList();

        keys.Should().ContainSingle()
            .Which.Should().Contain("/health/ready",
                "the dashboard should call the API healthy only once PostgreSQL and Redis answer");
    }

    [Fact]
    public void Ingest_IsOrchestrated_SoTheDashboardModelsEveryRuntimeProcess()
    {
        model.Resource("ingest").Should().BeAssignableTo<ProjectResource>();
    }

    [Fact]
    public void TheMailConnectionString_PointsAtTheSmtpSink_RatherThanAnExternalRelay()
    {
        RelationshipsOf("mail", "Reference").Should().BeEquivalentTo(["mailpit"]);
    }

    private ContainerResource Container(string name) =>
        model.Resource(name).Should().BeAssignableTo<ContainerResource>().Subject;

    private ContainerImageAnnotation ImageOf(string name) =>
        Container(name).Annotations.OfType<ContainerImageAnnotation>().Should().ContainSingle().Subject;

    private IReadOnlyList<string> RelationshipsOf(string name, string type) =>
        model.Resource(name)
            .Annotations.OfType<ResourceRelationshipAnnotation>()
            .Where(relationship => relationship.Type == type)
            .Select(relationship => relationship.Resource.Name)
            .ToList();
}
