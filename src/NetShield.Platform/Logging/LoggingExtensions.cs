using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetShield.Platform.Logging;

/// <summary>
/// Wires secret redaction into the logging pipeline.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Replaces the <see cref="ILoggerFactory"/> with one that redacts.
    /// </summary>
    /// <remarks>
    /// The swap is at the factory, not at a provider. A provider-level decorator would only
    /// cover the providers registered when it ran, and would silently stop covering the next one
    /// somebody adds — which is the failure mode ARCHITECTURE.md §8 exists to rule out. Every
    /// <c>ILogger&lt;T&gt;</c> resolves through <see cref="ILoggerFactory"/>, so replacing it
    /// covers the whole process.
    /// </remarks>
    public static IServiceCollection AddSecretRedaction(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton<SecretRedactor>();

        services.RemoveAll<ILoggerFactory>();
        services.AddSingleton<ILoggerFactory>(provider => new RedactingLoggerFactory(
            new LoggerFactory(
                provider.GetServices<ILoggerProvider>(),
                provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>(),
                provider.GetRequiredService<IOptions<LoggerFactoryOptions>>()),
            provider.GetRequiredService<SecretRedactor>()));

        return services;
    }
}
