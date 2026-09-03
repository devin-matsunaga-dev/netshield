using NetShield.Platform.Messaging;

namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// A handler that records what it was given, and fails when the test says to.
/// </summary>
public sealed class RecordingHandler(HandlerLog log) : IIntegrationEventHandler<DeviceProbed>
{
    public Task HandleAsync(DeviceProbed integrationEvent, CancellationToken cancellationToken)
    {
        log.Record(integrationEvent);

        return log.Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }
}
