using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Platform;
using NetShield.Platform.Logging;
using NetShield.Platform.Messaging;
using NetShield.Platform.Time;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers what <c>AddNetShieldPlatform</c> leaves behind in the container. These run against a
/// real host builder, because a registration that only works in a hand-built
/// <see cref="ServiceCollection"/> is a registration that has not been tested.
/// </summary>
public sealed class PlatformRegistrationTests
{
    [Fact]
    public void TheClock_IsTheSystemClock_AndReportsUtc()
    {
        using IHost host = BuildHost();

        IClock clock = host.Services.GetRequiredService<IClock>();

        clock.Should().BeOfType<SystemClock>();
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void EveryLogger_ComesBackRedacting()
    {
        using IHost host = BuildHost();

        // ILogger<T> resolves through ILoggerFactory, so replacing the factory is what makes
        // this true of loggers this package never sees.
        host.Services.GetRequiredService<ILogger<PlatformRegistrationTests>>()
            .Should().BeOfType<Logger<PlatformRegistrationTests>>();

        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Anything")
            .Should().BeOfType<RedactingLogger>();
    }

    [Fact]
    public void OutboxOptions_HaveTheDefaultsTheConventionsDescribe()
    {
        using IHost host = BuildHost();

        OutboxOptions options = host.Services.GetRequiredService<IOptions<OutboxOptions>>().Value;

        options.BatchSize.Should().Be(100);
        options.MaxAttempts.Should().Be(5);
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void OutboxOptions_BindFromConfiguration()
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["Outbox:BatchSize"] = "25",
            ["Outbox:PollInterval"] = "00:00:05"
        });

        OutboxOptions options = host.Services.GetRequiredService<IOptions<OutboxOptions>>().Value;

        options.BatchSize.Should().Be(25);
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OutboxOptions_ThatCannotWork_FailAtStartup_NotAtFirstDispatch()
    {
        using IHost host = BuildHost(new Dictionary<string, string?> { ["Outbox:BatchSize"] = "0" });

        Func<Task> start = () => host.StartAsync(TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<OptionsValidationException>(
            "a misconfigured dispatcher should stop the process, not silently deliver nothing");
    }

    [Fact]
    public void TheDispatcher_IsNotRegisteredUntilAHostAsksForIt()
    {
        using IHost host = BuildHost();

        host.Services.GetServices<IHostedService>().Should().NotContain(service => service is OutboxDispatcher,
            "only the API delivers events; a worker that publishes them must not also dispatch them");
    }

    [Fact]
    public void TheDispatcher_IsRegistered_WhenAHostAsksForIt()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.AddNetShieldPlatform();
        builder.AddOutboxDispatcher();

        using IHost host = builder.Build();

        host.Services.GetServices<IHostedService>().Should().ContainSingle(service => service is OutboxDispatcher);
    }

    private static IHost BuildHost(Dictionary<string, string?>? configuration = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        builder.AddNetShieldPlatform();

        return builder.Build();
    }
}
