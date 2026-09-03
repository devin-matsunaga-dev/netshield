using Microsoft.Extensions.Logging;

namespace NetShield.IntegrationTests.Identity;

/// <summary>One line a NetShield component wrote, as it reached the sink.</summary>
/// <param name="Level">The level it was written at.</param>
/// <param name="Category">The <c>ILogger&lt;T&gt;</c> category.</param>
/// <param name="Message">The rendered message, after redaction.</param>
/// <param name="Values">
/// The structured property values, rendered at the moment they were logged. Rendered eagerly
/// because ASP.NET Core's own request log is a lazy view over the <c>HttpContext</c>, and reading
/// it after the request has finished throws rather than returning what was written.
/// </param>
internal sealed record RecordedLog(
    LogLevel Level,
    string Category,
    string Message,
    IReadOnlyList<string> Values);
