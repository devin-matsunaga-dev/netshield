using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Microsoft.Extensions.DependencyInjection;

using NetShield.Platform.Logging;
using NetShield.Platform.Messaging;
using NetShield.Platform.Persistence;

namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// Covers the WP-0.3 criterion that an outbox round trip is exercised against a real database:
/// an event published inside a transaction is delivered if and only if that transaction
/// commits, is delivered once, and survives a handler that fails (ARCHITECTURE.md §5).
/// </summary>
public sealed class OutboxRoundTripTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Publish_ThenCommit_WritesExactlyOneOutboxRow()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);

        await PublishAsync(host, Probe("core-sw-01"), commit: true);

        IReadOnlyList<OutboxMessage> outbox = await host.ReadOutboxAsync(Cancellation);

        outbox.Should().ContainSingle();
        outbox[0].EventType.Should().Be(typeof(DeviceProbed).FullName);
        outbox[0].ProcessedAt.Should().BeNull("nothing is delivered until the dispatcher runs");
        outbox[0].Payload.Should().Contain("core-sw-01");
    }

    [Fact]
    public async Task Publish_WhenTheTransactionRollsBack_LeavesNothingToDeliver()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);

        await PublishAsync(host, Probe("core-sw-01"), commit: false);

        (await host.ReadOutboxAsync(Cancellation)).Should().BeEmpty(
            "the event and the domain change it describes are one write, or they are neither");

        (await host.DispatchOnceAsync(Cancellation)).Should().Be(0);
        host.Log.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_DeliversTheEvent_AndMarksTheRowProcessed()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);
        Guid deviceId = Guid.CreateVersion7();

        await PublishAsync(host, new DeviceProbed(deviceId, "core-sw-01"), commit: true);

        (await host.DispatchOnceAsync(Cancellation)).Should().Be(1);

        host.Log.Handled.Should().ContainSingle()
            .Which.Should().Be(new DeviceProbed(deviceId, "core-sw-01"),
                "the payload round-trips through jsonb unchanged");

        OutboxMessage row = (await host.ReadOutboxAsync(Cancellation)).Should().ContainSingle().Subject;
        row.ProcessedAt.Should().NotBeNull();
        row.Attempts.Should().Be(1);
        row.Error.Should().BeNull();
    }

    [Fact]
    public async Task Dispatch_RunAgain_DeliversNothingASecondTime()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);

        await PublishAsync(host, Probe("core-sw-01"), commit: true);
        await host.DispatchOnceAsync(Cancellation);

        (await host.DispatchOnceAsync(Cancellation)).Should().Be(0);
        host.Log.Handled.Should().ContainSingle("a delivered row is never picked up again");
    }

    [Fact]
    public async Task Dispatch_DeliversInTheOrderThePublisherWroteThem()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);

        await PublishAsync(host, Probe("core-sw-01"), commit: true);
        await PublishAsync(host, Probe("core-sw-02"), commit: true);
        await PublishAsync(host, Probe("core-sw-03"), commit: true);

        (await host.DispatchOnceAsync(Cancellation)).Should().Be(3);

        host.Log.Handled.Select(handled => handled.Hostname)
            .Should().Equal("core-sw-01", "core-sw-02", "core-sw-03");
    }

    [Fact]
    public async Task Dispatch_WhenTheHandlerFails_LeavesTheRowPendingForAnotherAttempt()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);
        host.Log.Failure = new InvalidOperationException("the notification channel is unreachable");

        await PublishAsync(host, Probe("core-sw-01"), commit: true);

        (await host.DispatchOnceAsync(Cancellation)).Should().Be(0);

        OutboxMessage row = (await host.ReadOutboxAsync(Cancellation)).Should().ContainSingle().Subject;
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.Error.Should().Contain("the notification channel is unreachable");
    }

    [Fact]
    public async Task Dispatch_AfterTheFailureClears_DeliversTheRowItCouldNotDeliverBefore()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);
        host.Log.Failure = new InvalidOperationException("the notification channel is unreachable");

        await PublishAsync(host, Probe("core-sw-01"), commit: true);
        await host.DispatchOnceAsync(Cancellation);

        host.Log.Failure = null;

        (await host.DispatchOnceAsync(Cancellation)).Should().Be(1);
        (await host.ReadOutboxAsync(Cancellation))[0].Error.Should().BeNull("a delivered row carries no failure");
    }

    [Fact]
    public async Task Dispatch_ParksARowThatKeepsFailing_RatherThanRetryingItForever()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);
        host.Log.Failure = new InvalidOperationException("the notification channel is unreachable");

        await PublishAsync(host, Probe("core-sw-01"), commit: true);

        OutboxOptions defaults = new();

        for (int attempt = 0; attempt < defaults.MaxAttempts; attempt++)
        {
            await host.DispatchOnceAsync(Cancellation);
        }

        int handledBeforeParking = host.Log.Handled.Count;
        handledBeforeParking.Should().Be(defaults.MaxAttempts);

        await host.DispatchOnceAsync(Cancellation);

        host.Log.Handled.Count.Should().Be(handledBeforeParking, "a parked row is left alone, not retried");

        OutboxMessage row = (await host.ReadOutboxAsync(Cancellation)).Should().ContainSingle().Subject;
        row.ProcessedAt.Should().BeNull("a parked row stays visible to an operator rather than vanishing");
        row.Attempts.Should().Be(defaults.MaxAttempts);
    }

    [Fact]
    public async Task Dispatch_StoresAFailureRedacted_BecauseSpecCoversTheDatabaseToo()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);
        host.Log.Failure = new InvalidOperationException("SSH to core-sw-01 failed with password=hunter2");

        await PublishAsync(host, Probe("core-sw-01"), commit: true);
        await host.DispatchOnceAsync(Cancellation);

        string? stored = (await host.ReadOutboxAsync(Cancellation))[0].Error;

        stored.Should().NotContain("hunter2", "SPEC.md §5 names the database alongside the log");
        stored.Should().Contain(SecretRedactor.Placeholder);
    }

    [Fact]
    public async Task Publish_ForAnEventTheHostDoesNotCarry_FailsAtThePublish()
    {
        await using OutboxHost host = await OutboxHost.StartAsync(postgres, Cancellation);
        await using AsyncServiceScope scope = host.CreateScope();

        IEventBus bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        Func<Task> publish = () => bus.PublishAsync(new UnregisteredEvent(), Cancellation);

        await publish.Should().ThrowAsync<InvalidOperationException>(
            "a row naming a type nothing can resolve could only ever fail, so it is never written");

        (await host.ReadOutboxAsync(Cancellation)).Should().BeEmpty();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DeviceProbed Probe(string hostname) => new(Guid.CreateVersion7(), hostname);

    /// <summary>
    /// Publishes inside an explicit transaction, which is the whole point of the outbox: the row
    /// and whatever domain change accompanies it are one write.
    /// </summary>
    private static async Task PublishAsync(OutboxHost host, DeviceProbed integrationEvent, bool commit)
    {
        await using AsyncServiceScope scope = host.CreateScope();

        PlatformDbContext context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        IEventBus bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(Cancellation);

        await bus.PublishAsync(integrationEvent, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        if (commit)
        {
            await transaction.CommitAsync(Cancellation);
        }
        else
        {
            await transaction.RollbackAsync(Cancellation);
        }
    }
}
