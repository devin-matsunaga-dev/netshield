using NetShield.Platform.Results;

namespace NetShield.Inventory.Clients;

/// <summary>
/// Every refusal the client handlers can return, in one place, so the codes a caller branches on
/// are visible together rather than spread across six files (CONVENTIONS.md §4).
/// </summary>
internal static class ClientErrors
{
    /// <summary>The code a caller sees when the client is not there.</summary>
    internal const string NotFoundCode = "client.not-found";

    /// <summary>The code a caller sees when a client walk is already queued for the device.</summary>
    internal const string WalkOutstandingCode = "client.walk-outstanding";

    /// <summary>The code a caller sees when the address they asked to resolve is not one.</summary>
    internal const string InvalidAddressCode = "client.invalid-address";

    /// <summary>The code a caller sees when the instant they asked about has not happened.</summary>
    internal const string FutureInstantCode = "client.future-instant";

    internal static Error NotFound(Guid id) =>
        Error.NotFound(NotFoundCode, $"No client with id {id}.");

    /// <summary>
    /// A walk of this device's tables is already queued or leased.
    /// </summary>
    /// <remarks>
    /// The same rule the on-demand fingerprint walk applies, and for the same reason: a person
    /// clicking twice must not become two collectors reading one device's forwarding database and
    /// having their results applied in whichever order they came back — which for an interval
    /// table would mean two walks each closing the other's bindings.
    /// </remarks>
    internal static Error WalkOutstanding(Guid deviceId) =>
        Error.Conflict(
            WalkOutstandingCode,
            $"A client walk of device {deviceId} is already queued.");

    internal static Error InvalidAddress(string? value) =>
        Error.Validation(
            InvalidAddressCode,
            "The address to resolve is not an IP address.",
            new Dictionary<string, string[]>
            {
                ["ipAddress"] = [string.IsNullOrWhiteSpace(value)
                    ? "An address is required."
                    : "Must be an IPv4 or IPv6 address."]
            });

    /// <summary>
    /// Resolving an instant in the future is refused rather than answered.
    /// </summary>
    /// <remarks>
    /// Every open binding covers every future instant, so the query would happily answer — with
    /// the current holder, dressed up as a fact about a moment that has not happened. A caller
    /// who asked for one has a clock problem, and telling them is more use than a confident
    /// answer.
    /// </remarks>
    internal static Error FutureInstant() =>
        Error.Validation(
            FutureInstantCode,
            "Cannot resolve an address at an instant in the future.",
            new Dictionary<string, string[]>
            {
                ["at"] = ["Must be now or earlier."]
            });
}
