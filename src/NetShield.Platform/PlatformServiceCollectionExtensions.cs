using Microsoft.AspNetCore.Authorization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Messaging;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Cryptography;
using NetShield.Platform.Logging;
using NetShield.Platform.Messaging;
using NetShield.Platform.Time;

namespace NetShield.Platform;

/// <summary>
/// Registers the platform primitives every NetShield process shares: secret redaction, the
/// clock, and the writing half of the transactional outbox.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds the cross-cutting services. A host that also serves HTTP calls
    /// <c>AddNetShieldProblemDetails</c>; a host that should deliver events calls
    /// <see cref="AddOutboxDispatcher{TBuilder}"/>.
    /// </summary>
    public static TBuilder AddNetShieldPlatform<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSecretRedaction();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IClock, SystemClock>();

        builder.Services.AddOptions<OutboxOptions>()
            .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddSingleton<IntegrationEventRegistry>();
        builder.Services.TryAddSingleton<OutboxEnlistment>();
        builder.Services.TryAddScoped<IEventBus, OutboxEventBus>();
        builder.Services.TryAddScoped<OutboxProcessor>();

        return builder;
    }

    /// <summary>
    /// Adds envelope encryption for data at rest: the key ring from configuration and the
    /// encryptor that wraps a per-value data key with it (ARCHITECTURE.md §8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="AddNetShieldPlatform{TBuilder}"/> because a key-encryption key is
    /// the highest-value secret NetShield holds and no process should be given one it has no use
    /// for. The schema step does not call this: applying a migration encrypts nothing, and a
    /// migrator that demanded the key would be one more place the key has to be delivered to.
    /// </para>
    /// <para>
    /// A module that stores a secret calls this itself, so that registering the module and being
    /// able to seal what it stores are the same act.
    /// </para>
    /// </remarks>
    public static TBuilder AddNetShieldEnvelopeEncryption<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<EnvelopeEncryptionOptions>()
            .Bind(builder.Configuration.GetSection(EnvelopeEncryptionOptions.SectionName))
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EnvelopeEncryptionOptions>,
                EnvelopeEncryptionOptionsValidator>());

        // Singletons: the ring decodes its keys once, and the encryptor holds nothing per call.
        builder.Services.TryAddSingleton<KeyEncryptionKeyRing>();
        builder.Services.TryAddSingleton<IEnvelopeEncryptor, AesGcmEnvelopeEncryptor>();

        return builder;
    }

    /// <summary>
    /// Adds RBAC: the permission policies, the two authorization requirements every authenticated
    /// endpoint carries, and the accessors a module checks a resource with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deny by default. The fallback policy applies to any endpoint that declares no
    /// authorization of its own, so a route added without a policy is refused rather than
    /// published — the failure mode is a 401 in development, not an open endpoint in production.
    /// A route that really is public says <c>AllowAnonymous()</c> and says it in the diff.
    /// </para>
    /// <para>
    /// Called by a host that serves the API. It is separate from
    /// <see cref="AddNetShieldPlatform{TBuilder}"/> because <c>NetShield.Ingest</c> receives
    /// syslog and flow records from devices and has no user to authorize.
    /// </para>
    /// </remarks>
    public static TBuilder AddNetShieldAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpContextAccessor();

        builder.Services.TryAddSingleton<ICurrentUser, CurrentUser>();
        builder.Services.TryAddScoped<IResourceGuard, ResourceGuard>();

        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, PasswordChangeNotPendingHandler>();
        builder.Services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        builder.Services.AddAuthorization(options =>
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PasswordChangeNotPendingRequirement())
                .Build();

            options.DefaultPolicy = policy;
            options.FallbackPolicy = policy;
        });

        return builder;
    }

    /// <summary>
    /// Adds the append-only audit log: the per-request collector a handler enriches, and the
    /// writer the middleware appends through. The middleware itself is added by
    /// <c>UseNetShieldAudit</c>.
    /// </summary>
    public static TBuilder AddNetShieldAudit<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<AuditContext>();
        builder.Services.TryAddScoped<IAuditContext>(provider => provider.GetRequiredService<AuditContext>());
        builder.Services.TryAddScoped<IAuditLog, AuditLog>();

        return builder;
    }

    /// <summary>
    /// Starts the background loop that delivers committed outbox rows. Exactly one process in a
    /// deployment runs this — the API (ARCHITECTURE.md §2 puts every decision there).
    /// </summary>
    public static TBuilder AddOutboxDispatcher<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<OutboxDispatcher>();

        return builder;
    }

    /// <summary>
    /// Declares an event type this host can publish and deliver. An event that is not declared
    /// cannot be published, which is what stops an outbox row naming a type nothing can resolve.
    /// </summary>
    public static IServiceCollection AddIntegrationEvent<TEvent>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new IntegrationEventRegistration(typeof(TEvent)));

        return services;
    }

    /// <summary>Registers a handler for an event this host carries.</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();

        return services;
    }
}
