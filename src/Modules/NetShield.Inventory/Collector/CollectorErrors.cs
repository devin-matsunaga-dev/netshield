using NetShield.Platform.Results;

namespace NetShield.Inventory.Collector;

/// <summary>
/// Every refusal the collector contract can answer with, in one place (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// No message here names a credential, a key, or the shared secret. These are read by the
/// collector and land in its log, which is one of the places SPEC.md §5 says a credential must
/// never reach.
/// </remarks>
internal static class CollectorErrors
{
    internal const string UnknownJobReason = "unknown-job";
    internal const string StaleLeaseReason = "stale-lease";
    internal const string ResultTooLargeReason = "result-too-large";

    internal const string TooManyResultsCode = "collector.too-many-results";
    internal const string ParametersTooLargeCode = "collector.parameters-too-large";
    internal const string UnknownDeviceCode = "collector.unknown-device";
    internal const string UnknownCredentialProfileCode = "collector.unknown-credential-profile";

    internal static Error TooManyResults(int limit) =>
        Error.Unprocessable(
            TooManyResultsCode,
            $"A submission may carry at most {limit} results.");

    internal static Error ParametersTooLarge(int limit) =>
        Error.Unprocessable(
            ParametersTooLargeCode,
            $"Job parameters may be at most {limit} bytes of JSON.");

    internal static Error UnknownDevice(Guid deviceId) =>
        Error.NotFound(UnknownDeviceCode, $"No device with id {deviceId}.");

    internal static Error UnknownCredentialProfile(Guid credentialProfileId) =>
        Error.NotFound(
            UnknownCredentialProfileCode,
            $"No credential profile with id {credentialProfileId}.");
}
