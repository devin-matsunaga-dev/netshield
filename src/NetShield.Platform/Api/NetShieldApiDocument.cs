using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetShield.Platform.Api;

/// <summary>
/// The OpenAPI description of the NetShield API, and the route that serves it.
/// </summary>
/// <remarks>
/// <para>
/// CONVENTIONS.md §4 makes the document generated rather than written, and makes the TypeScript
/// client generated from the document rather than from the endpoints. This is the one place the
/// document is configured, so the copy committed for the client is the copy the API serves.
/// </para>
/// <para>
/// It lives in <c>NetShield.Platform</c> rather than in the composition root because the check
/// that keeps the committed copy honest has to be able to produce the same document, and
/// ARCHITECTURE.md §4 lets nothing reference <c>NetShield.Web.Host</c>.
/// </para>
/// </remarks>
public static class NetShieldApiDocument
{
    /// <summary>The document name, matching the <c>/api/v1</c> the endpoints hang from.</summary>
    public const string Name = "v1";

    /// <summary>The route the document is served from, when it is served at all.</summary>
    public const string Route = "/openapi/{documentName}.json";

    /// <summary>The prefix an endpoint's path must carry to appear in the document.</summary>
    private const string ApiPrefix = "api/";

    /// <summary>
    /// Adds the OpenAPI document describing every <c>/api/</c> endpoint the host maps.
    /// </summary>
    /// <remarks>
    /// Only <c>/api/</c> paths are described. The health endpoints are for a container probe and
    /// the Aspire dashboard, not for a client, and a generated client should carry no method for
    /// them.
    /// </remarks>
    public static TBuilder AddNetShieldApiDocument<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOpenApi(Name, options =>
        {
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith(ApiPrefix, StringComparison.Ordinal) == true;

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "NetShield API";
                document.Info.Version = Name;
                document.Info.Description =
                    "The NetShield API. Every path is versioned under /api/v1 and every error is "
                    + "an RFC 9457 problem document.";

                // The document travels to a generator, not to a browser, and the SPA is served
                // from the same origin as the API. A server list would only ever be wrong.
                document.Servers?.Clear();

                return Task.CompletedTask;
            });
        });

        return builder;
    }

    /// <summary>
    /// Serves the document at <see cref="Route"/>, in development only.
    /// </summary>
    /// <remarks>
    /// Anonymous by necessity — the API denies by default (ARCHITECTURE.md §8) and a document
    /// that required a session could not be read by a generator. It is therefore not published
    /// outside development: the committed copy under <c>openapi/</c> is what the client is built
    /// from, and a deployment has no reason to describe itself to the network.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "ArgumentNullException.ThrowIfNull is the guard.")]
    public static WebApplication MapNetShieldApiDocument(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.Environment.IsDevelopment())
        {
            application.MapOpenApi(Route).AllowAnonymous();
        }

        return application;
    }
}
