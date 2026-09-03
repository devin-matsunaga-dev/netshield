using Microsoft.Extensions.Logging;

namespace NetShield.Platform.Logging;

/// <summary>
/// The factory every <see cref="ILogger{TCategoryName}"/> in a NetShield process comes from.
/// It hands back <see cref="RedactingLogger"/> instances, so redaction covers every provider
/// registered before or after this factory was built.
/// </summary>
internal sealed class RedactingLoggerFactory(ILoggerFactory inner, SecretRedactor redactor) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) =>
        new RedactingLogger(inner.CreateLogger(categoryName), redactor);

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();
}
