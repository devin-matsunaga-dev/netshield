using System.Text.Json;

namespace NetShield.IntegrationTests.Identity;

/// <summary>What a call to the API produced, flattened so a test can assert on it directly.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Body">The response body, as it came off the wire.</param>
/// <param name="SetCookies">Every <c>Set-Cookie</c> header, unparsed.</param>
internal sealed record ApiResponse(int Status, string Body, IReadOnlyList<string> SetCookies)
{
    /// <summary>The body as JSON.</summary>
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;

    /// <summary>One member of the body, or <see langword="null"/> when it is absent.</summary>
    public string? Member(string name) =>
        Json.TryGetProperty(name, out JsonElement value) ? value.ToString() : null;

    /// <summary>The <c>Set-Cookie</c> header that writes <paramref name="name"/>, if any.</summary>
    public string? CookieHeader(string name) =>
        SetCookies.FirstOrDefault(header => header.StartsWith($"{name}=", StringComparison.Ordinal));
}
