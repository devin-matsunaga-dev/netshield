using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using NetShield.IntegrationTests.Identity;

namespace NetShield.IntegrationTests.Collector;

/// <summary>
/// A client that presents the collector's shared secret and holds no cookie.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="SessionClient"/> with an extra header. The point of the internal
/// contract is that the two credentials are separate — a collector holds a bearer token and no
/// session, a person holds a session and no token — and a harness that could hold both would
/// make it easy to write a test that proved less than it looked like it did.
/// </remarks>
internal sealed class CollectorClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Uri _baseAddress;
    private readonly HttpClient _client;

    public CollectorClient(Uri baseAddress, string? sharedSecret)
    {
        _baseAddress = baseAddress;
        _client = Create(baseAddress, sharedSecret);
    }

    public Task<ApiResponse> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(_client, HttpMethod.Get, path, content: null, cancellationToken);

    public Task<ApiResponse> PostAsync<TBody>(string path, TBody body, CancellationToken cancellationToken) =>
        SendAsync(_client, HttpMethod.Post, path, JsonContent.Create(body, options: Json), cancellationToken);

    /// <summary>Sends a body no request record could express, so a test can ask what happens.</summary>
    public Task<ApiResponse> PostRawAsync(string path, string json, CancellationToken cancellationToken) =>
        SendAsync(
            _client,
            HttpMethod.Post,
            path,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);

    /// <summary>
    /// Sends one call under a different credential — a wrong secret, or none at all — on a client
    /// of its own, so this one's header cannot leak into it.
    /// </summary>
    public async Task<ApiResponse> WithSecretAsync(
        string? secret,
        string path,
        CancellationToken cancellationToken)
    {
        using HttpClient client = Create(_baseAddress, secret);

        return await SendAsync(client, HttpMethod.Get, path, content: null, cancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private static HttpClient Create(Uri baseAddress, string? sharedSecret)
    {
        HttpClient client = new() { BaseAddress = baseAddress };

        if (sharedSecret is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sharedSecret);
        }

        return client;
    }

    private static async Task<ApiResponse> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, path) { Content = content };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ApiResponse((int)response.StatusCode, body, []);
    }
}
