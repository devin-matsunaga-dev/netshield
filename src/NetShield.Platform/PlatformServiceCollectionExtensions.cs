using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using NetShield.Contracts.Messaging;

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
        builder.Services.TryAddScoped<IEventBus, OutboxEventBus>();
        builder.Services.TryAddScoped<OutboxProcessor>();

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
