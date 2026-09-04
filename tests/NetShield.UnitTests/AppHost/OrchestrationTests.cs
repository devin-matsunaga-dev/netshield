using System.Text.RegularExpressions;

using Aspire.Hosting.ApplicationModel;

using FluentAssertions;

using NetShield.AppHost;

using NetShield.Platform.Cryptography;

namespace NetShield.UnitTests.AppHost;

/// <summary>
/// Asserts the shape of the development orchestration declared by <c>NetShield.AppHost</c>:
/// the stores from ARCHITECTURE.md §3, their persistence, and the wiring that carries their
/// connection strings into the API.
/// </summary>
public sealed partial class OrchestrationTests(AppHostModel model) : IClassFixture<AppHostModel>
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

        references.Should().BeEquivalentTo(
            ["netshield", "cache", "mail", "identity-admin-password", "credential-kek"],
            "the API reads PostgreSQL, Redis and the SMTP sink, Aspire supplies all three, and both "
            + "the first-run administrator's password and the credential key-encryption key reach it "
            + "the same way rather than from a file");
    }

    /// <summary>
    /// The key-encryption key goes to the API and to nothing else. The schema step applies
    /// migrations and encrypts nothing, and a process is not handed the highest-value secret in
    /// the system for a job that has no use for it (ARCHITECTURE.md §8).
    /// </summary>
    [Fact]
    public void TheMigrator_DoesNotReceiveTheCredentialKey_BecauseItEncryptsNothing() =>
        RelationshipsOf("db-migrator", "Reference").Should().NotContain("credential-kek");

    [Fact]
    public void TheCredentialKey_IsASecretParameter_SoItIsNeverWrittenIntoTheRepository()
    {
        ParameterResource parameter = model.Resource("credential-kek")
            .Should().BeAssignableTo<ParameterResource>().Subject;

        parameter.Secret.Should().BeTrue(
            "it wraps every stored device credential; the dashboard must mask it and it must not "
            + "be written to the manifest in plaintext (SPEC.md §5)");
    }

    /// <summary>
    /// The configuration contract is "this value is a 256-bit key", not "this string becomes
    /// one" — so what the AppHost generates has to decode to exactly the length the key ring
    /// demands, or development would come up with a ring the host refuses to start on.
    /// </summary>
    [Fact]
    public void TheGeneratedCredentialKey_IsBase64OfExactlyThirtyTwoBytes()
    {
        string generated = new Base64KeyParameterDefault(KeyEncryptionKeyRing.KeyLengthBytes)
            .GetDefaultValue();

        Convert.FromBase64String(generated).Should().HaveCount(KeyEncryptionKeyRing.KeyLengthBytes);
    }

    /// <summary>
    /// Two runs must not produce the same key, and a fresh one every run is not the answer
    /// either — the parameter is persisted, so what this guards is that the generator is random
    /// rather than that the value changes between runs.
    /// </summary>
    [Fact]
    public void TheGeneratedCredentialKey_IsDifferentEveryTimeItIsGenerated()
    {
        Base64KeyParameterDefault generator = new(KeyEncryptionKeyRing.KeyLengthBytes);

        generator.GetDefaultValue().Should().NotBe(generator.GetDefaultValue());
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

        waits.Should().BeEquivalentTo(["postgres", "netshield", "cache", "mailpit", "db-migrator"]);
    }

    /// <summary>
    /// The schema step is the same project as the API, run with <c>--migrate</c> — not a sixth
    /// project, which would change the five-process model in ARCHITECTURE.md §2.
    /// </summary>
    [Fact]
    public void TheMigrator_IsTheApiProject_RunWithTheMigrateArgument()
    {
        IResource migrator = model.Resource("db-migrator");

        migrator.Should().BeAssignableTo<ProjectResource>();

        IReadOnlyList<object> arguments = migrator.Annotations.OfType<CommandLineArgsCallbackAnnotation>()
            .Select(annotation => annotation)
            .Cast<object>()
            .ToList();

        arguments.Should().NotBeEmpty("the resource has to be told to migrate rather than to serve");
    }

    /// <summary>
    /// Waiting for the migrator to <em>finish</em> rather than to start is the whole point: the
    /// API must never come up against a half-applied schema, and on a fresh volume the
    /// administrator it needs is created by that step.
    /// </summary>
    [Fact]
    public void WebHost_WaitsForTheMigrator_ToComplete()
    {
        WaitAnnotation wait = model.Resource("web-host")
            .Annotations.OfType<WaitAnnotation>()
            .Should().ContainSingle(annotation => annotation.Resource.Name == "db-migrator").Subject;

        wait.WaitType.Should().Be(WaitType.WaitForCompletion);
    }

    /// <summary>
    /// The migrator binds no socket, so it has no readiness to report and must not be given a
    /// health check that would keep the dashboard waiting for a process that has already exited.
    /// </summary>
    [Fact]
    public void TheMigrator_HasNoHealthCheck()
    {
        model.Resource("db-migrator")
            .Annotations.OfType<HealthCheckAnnotation>()
            .Should().BeEmpty();
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
    public void WebClient_IsOrchestrated_SoOneCommandBringsUpTheWholeStack()
    {
        model.Resource("web-client").Should().NotBeNull(
            "ARCHITECTURE.md §2 counts the SPA among the runtime processes, and a developer "
            + "should not have to start it by hand");
    }

    [Fact]
    public void WebClient_ReferencesTheApi_SoTheDevServerLearnsItsAddressRatherThanCarryingOne()
    {
        // Distinct: the SPA is told the address twice — once as Aspire's service-discovery
        // variable and once under a name a shell will pass on — and each is a reference.
        RelationshipsOf("web-client", "Reference").Distinct().Should().BeEquivalentTo(["web-host"],
            "the Vite proxy target reaches the SPA from the model rather than from a literal; "
            + "SPEC.md §5 keeps the address out of the repository");
    }

    [Fact]
    public async Task WebClient_LearnsTheApiAddressUnderAShellSafeName_BecauseNpmRunsItsScriptThroughSh()
    {
        // `npm run dev` executes its script through `sh -c`, and a POSIX shell exports only the
        // variables whose names are valid shell identifiers. So the dev server never sees
        // Aspire's own `services__web-host__http__0` — the hyphens make it one a shell drops, in
        // silence, and the only symptom is /api answering with index.html.
        IReadOnlyList<string> names = await AppHostModel.EnvironmentNamesOf(model.Resource("web-client"));

        names.Should().Contain("NETSHIELD_API_URL",
            "vite.config.ts reads this name, and it is one a shell will pass on");

        names.Should().Contain(name => !ShellIdentifier().IsMatch(name),
            "this test is only worth running while a name that cannot survive `sh` is still "
            + "being published; if none is, the explicit variable above can go");
    }

    [Fact]
    public void TheServiceDiscoveryNameForTheApi_CannotSurviveAShell_WhichIsWhyTheSpaIsToldTwice()
    {
        // The reason the line above exists, held where a reader can see it. Rename `web-host` to
        // something without a hyphen and this fails, which is the signal to delete both.
        ShellIdentifier().IsMatch($"services__{model.Resource("web-host").Name}__http__0")
            .Should().BeFalse();
    }

    [Fact]
    public void WebClient_WaitsForItsPackagesAndTheApi_SoAFreshCloneComesUpWithOneCommand()
    {
        model.Resource("web-client")
            .Annotations.OfType<WaitAnnotation>()
            .Select(wait => wait.Resource.Name)
            .Should().BeEquivalentTo(["web-client-installer", "web-host"],
                "npm install runs first, and the dev server's proxy needs somewhere to send its "
                + "first request");
    }

    [Fact]
    public void TheMailConnectionString_PointsAtTheSmtpSink_RatherThanAnExternalRelay()
    {
        RelationshipsOf("mail", "Reference").Should().BeEquivalentTo(["mailpit"]);
    }

    /// <summary>What a POSIX shell will export: a letter or underscore, then word characters.</summary>
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ShellIdentifier();

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
