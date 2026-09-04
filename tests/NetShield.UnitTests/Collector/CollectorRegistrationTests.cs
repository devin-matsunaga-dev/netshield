using FluentAssertions;

using FluentValidation;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NetShield.Inventory;
using NetShield.Inventory.Collector;
using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Collector.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform;
using NetShield.Platform.Authentication;
using NetShield.Platform.Persistence;

namespace NetShield.UnitTests.Collector;

/// <summary>
/// Registering the Inventory module is what makes the internal contract authenticable.
/// </summary>
/// <remarks>
/// The same shape WP-1.2 chose for the key ring, for the same reason: a host that added the
/// endpoints without the secret would start, pass its health checks, and refuse — or worse,
/// admit — the first collector that asked.
/// </remarks>
public sealed class CollectorRegistrationTests
{
    private const string Secret = "registration-test-collector-secret-00000000";

    /// <summary>A fixture key-encryption key: base64 of the bytes 0x00 to 0x1f, in order.</summary>
    private const string KeyEncryptionKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public void AddNetShieldInventory_RegistersTheCollectorHandlers()
    {
        using IHost host = BuildHost();

        using IServiceScope scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<LeaseCollectorJobsHandler>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<SubmitCollectorResultsHandler>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<RecordHeartbeatHandler>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ICollectorJobQueue>().Should().BeOfType<CollectorJobQueue>();
    }

    [Fact]
    public void AddNetShieldInventory_RegistersAValidatorForEveryCollectorRequestShape()
    {
        using IHost host = BuildHost();

        using IServiceScope scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IValidator<CollectorResultsRequest>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IValidator<CollectorHeartbeatRequest>>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddNetShieldInventory_RegistersTheCollectorAuthenticationScheme()
    {
        using IHost host = BuildHost();

        AuthenticationScheme? scheme = await host.Services
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(CollectorIdentity.Scheme);

        scheme.Should().NotBeNull();
        scheme!.HandlerType.Should().Be<CollectorAuthenticationHandler>();
    }

    [Fact]
    public async Task TheCollectorPolicy_NamesTheSchemeSoASessionCannotSatisfyIt()
    {
        using IHost host = BuildHost();

        AuthorizationPolicy? policy = await host.Services
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(CollectorIdentity.PolicyName);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Equal(CollectorIdentity.Scheme);
    }

    [Fact]
    public async Task AHostWithNoSharedSecret_RefusesToStart()
    {
        using IHost host = BuildHost(secret: null);

        Func<Task> start = () => host.StartAsync(TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<OptionsValidationException>(
            "a host that served /internal/collector to whoever asked would be the failure mode a "
            + "default value creates on the one installation that forgot to set it");
    }

    private static IHost BuildHost(string? secret = Secret)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Security:CredentialEncryption:ActiveKeyId"] = "test",
            ["Security:CredentialEncryption:Keys:test"] = KeyEncryptionKey
        };

        if (secret is not null)
        {
            settings["Collector:SharedSecret"] = secret;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddLogging();

        // Configured with no provider: this asks what the module registers, and nothing here
        // opens a connection. The composition root is what knows where the database is
        // (SPEC.md §5).
        builder.Services.AddDbContext<InventoryDbContext>(options => options.UseInventoryConventions());
        builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNetShieldConventions());

        builder.AddNetShieldPlatform();
        builder.AddNetShieldAuthorization();
        builder.AddNetShieldAudit();
        builder.AddNetShieldInventory();

        return builder.Build();
    }
}
