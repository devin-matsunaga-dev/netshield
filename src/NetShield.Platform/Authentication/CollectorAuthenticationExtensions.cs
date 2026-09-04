using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetShield.Platform.Authentication;

/// <summary>
/// Registers the collector's shared-secret scheme, and the one way an endpoint asks for it.
/// </summary>
/// <remarks>
/// It lives in <c>NetShield.Platform</c> because ARCHITECTURE.md §4 puts auth there and §8 says
/// bearer tokens are how the collector and any later API integration authenticate — a second
/// integration reuses the shape rather than inventing one inside whichever module it happens to
/// serve.
/// </remarks>
public static class CollectorAuthenticationExtensions
{
    /// <summary>
    /// Adds the collector authentication scheme and its authorization policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the module that serves the internal contract, so that registering the endpoints
    /// and being able to authenticate them are one act — the same reasoning that put
    /// <c>AddNetShieldEnvelopeEncryption</c> inside <c>AddNetShieldInventory</c>. It also keeps
    /// the shared secret away from the schema step, which serves nothing.
    /// </para>
    /// <para>
    /// The policy requires the collector scheme and nothing else. It deliberately does not carry
    /// <c>PasswordChangeNotPendingRequirement</c>: that requirement is about a person who owes a
    /// password change, and there is no person here. Naming the scheme explicitly is what stops
    /// a cookie session satisfying <c>RequireAuthenticatedUser</c> on these routes.
    /// </para>
    /// </remarks>
    public static TBuilder AddNetShieldCollectorAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<CollectorAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(CollectorAuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, CollectorAuthenticationHandler>(
                CollectorIdentity.Scheme,
                configureOptions: null);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(CollectorIdentity.PolicyName, policy => policy
                .AddAuthenticationSchemes(CollectorIdentity.Scheme)
                .RequireAuthenticatedUser());

        return builder;
    }

    /// <summary>
    /// Refuses the request unless it presented the collector's shared secret.
    /// </summary>
    /// <remarks>
    /// The counterpart of <c>RequirePermission</c>, and named so that a reader of an endpoint
    /// file can see at a glance which of the two credentials a route accepts.
    /// </remarks>
    public static TBuilder RequireCollector<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(CollectorIdentity.PolicyName);

        return builder;
    }
}
