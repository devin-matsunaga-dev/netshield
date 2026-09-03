namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// What the handler did, and what it should do next. Registered as a singleton so a test can
/// read it after the scope the handler ran in has gone.
/// </summary>
public sealed class HandlerLog
{
    private readonly List<DeviceProbed> _handled = [];

    /// <summary>Every event the handler received, in the order it received them.</summary>
    public IReadOnlyList<DeviceProbed> Handled
    {
        get
        {
            lock (_handled)
            {
                return [.. _handled];
            }
        }
    }

    /// <summary>Set to make the next handled event fail, as a downstream outage would.</summary>
    public Exception? Failure { get; set; }

    internal void Record(DeviceProbed integrationEvent)
    {
        lock (_handled)
        {
            _handled.Add(integrationEvent);
        }
    }
}
