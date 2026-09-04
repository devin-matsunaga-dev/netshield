using System.Net.Http.Json;
using System.Text.Json;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// An HTTP client that keeps cookies by hand.
/// </summary>
/// <remarks>
/// <see cref="System.Net.CookieContainer"/> will not send a <c>Secure</c> cookie over <c>http</c>,
/// and the test host has no certificate — so it would refuse to replay exactly the cookies this
/// package exists to set. Holding them here also lets a test assert on the raw
/// <c>Set-Cookie</c> header, which is where <c>HttpOnly</c>, <c>Secure</c> and <c>SameSite</c>
/// actually live.
/// </remarks>
internal sealed class SessionClient(HttpClient client) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    /// <summary>The value the browser would currently hold for <paramref name="name"/>.</summary>
    public string? Cookie(string name) => _cookies.GetValueOrDefault(name);

    /// <summary>Replaces a stored cookie, to imitate a client replaying an old one.</summary>
    public void SetCookie(string name, string value) => _cookies[name] = value;

    public Task<ApiResponse> PostAsync<TBody>(string path, TBody body, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json)
        }, cancellationToken);

    public Task<ApiResponse> PostAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, path), cancellationToken);

    public Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    public Task<ApiResponse> PutAsync<TBody>(string path, TBody body, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: Json)
        }, cancellationToken);

    public Task<ApiResponse> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, path), cancellationToken);

    /// <summary>
    /// Sends a body the request shape could not express — a member of the wrong type, a member
    /// the record does not have — so a test can ask what the API does with it.
    /// </summary>
    public Task<ApiResponse> PostRawAsync(string path, string json, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        }, cancellationToken);

    public void Dispose() => client.Dispose();

    private async Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(pair => $"{pair.Key}={pair.Value}")));
            }

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            // NonValidated, because the framework's Set-Cookie parser splits on the comma inside
            // an expiry date and hands back fragments rather than headers.
            IReadOnlyList<string> setCookies = response.Headers.NonValidated.TryGetValues("Set-Cookie", out var values)
                ? [.. values]
                : [];

            foreach (string header in setCookies)
            {
                Apply(header);
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new ApiResponse((int)response.StatusCode, body, setCookies);
        }
    }

    private void Apply(string setCookieHeader)
    {
        string pair = setCookieHeader.Split(';')[0];
        int separator = pair.IndexOf('=', StringComparison.Ordinal);

        if (separator <= 0)
        {
            return;
        }

        string name = pair[..separator];
        string value = pair[(separator + 1)..];

        if (value.Length == 0)
        {
            _cookies.Remove(name);
            return;
        }

        _cookies[name] = value;
    }
}
