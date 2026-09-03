using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetShield.Platform.Logging;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers ARCHITECTURE.md §8 and SPEC.md §5: no credential reaches a logging sink, and the
/// guarantee comes from the pipeline rather than from every call site remembering.
/// </summary>
public sealed class SecretRedactionTests
{
    [Fact]
    public void APropertyNamedLikeASecret_IsEmittedRedacted()
    {
        using RedactedLog log = RedactedLog.Create();

        log.Logger.LogInformation("Authenticating {User} with {Password}", "admin", "hunter2");

        LogLine line = log.Single();
        line.Message.Should().NotContain("hunter2").And.Contain(SecretRedactor.Placeholder);
        line.Property("Password").Should().Be(SecretRedactor.Placeholder);
    }

    [Fact]
    public void TheRestOfTheLine_SurvivesRedaction()
    {
        using RedactedLog log = RedactedLog.Create();

        log.Logger.LogInformation("Authenticating {User} with {Password}", "admin", "hunter2");

        LogLine line = log.Single();
        line.Property("User").Should().Be("admin");
        line.Message.Should().StartWith("Authenticating admin with ");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("SnmpCommunity")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("RefreshToken")]
    [InlineData("PrivateKey")]
    [InlineData("Passphrase")]
    [InlineData("Authorization")]
    [InlineData("SessionId")]
    public void EverySecretShapedPropertyName_LosesItsValue(string propertyName)
    {
        using RedactedLog log = RedactedLog.Create();

        log.Logger.Log(
            LogLevel.Information,
            new EventId(1),
            new List<KeyValuePair<string, object?>>
            {
                new(propertyName, "s3cr3t-value"),
                new("{OriginalFormat}", $"probe {{{propertyName}}}")
            },
            exception: null,
            (_, _) => $"probe s3cr3t-value");

        LogLine line = log.Single();
        line.Property(propertyName).Should().Be(SecretRedactor.Placeholder);
        line.Message.Should().NotContain("s3cr3t-value");
    }

    [Fact]
    public void ASecretInterpolatedIntoTheMessage_IsStillRemoved()
    {
        using RedactedLog log = RedactedLog.Create();

        // The value carries its own name, which is what a config dump or a CLI banner looks like.
        log.Logger.LogWarning("Rejected connection using {Detail}", "user=admin password=hunter2 db=netshield");

        LogLine line = log.Single();
        line.Message.Should().NotContain("hunter2").And.Contain("user=admin");
        line.Property("Detail").Should().Be($"user=admin password={SecretRedactor.Placeholder} db=netshield");
    }

    [Fact]
    public void ABearerTokenAnywhereInTheText_IsRemoved()
    {
        using RedactedLog log = RedactedLog.Create();

        log.Logger.LogInformation("Header {Header}", "Bearer eyJhbGciOiJIUzI1NiJ9.abc123");

        log.Single().Message.Should().NotContain("eyJhbGciOiJIUzI1NiJ9").And.Contain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void APrivateKeyBlock_IsRemovedWhole()
    {
        using RedactedLog log = RedactedLog.Create();

        log.Logger.LogInformation(
            "Loaded {Material}",
            "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXk\n-----END OPENSSH PRIVATE KEY-----");

        log.Single().Message.Should().NotContain("b3BlbnNzaC1rZXk").And.Contain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void ALineWithNoSecret_ReachesTheSinkAsTheCallerBuiltIt()
    {
        using RedactedLog log = RedactedLog.Create();

        log.Logger.LogInformation("Polled {DeviceCount} devices in {Elapsed:0.0} ms", 42, 12.34);

        LogLine line = log.Single();
        line.Message.Should().Be("Polled 42 devices in 12.3 ms", "an untouched line keeps its format specifiers");
        line.Property("DeviceCount").Should().Be(42, "and its values keep their types for structured sinks");
    }

    [Fact]
    public void AScopeCarryingASecret_IsRedactedToo()
    {
        using RedactedLog log = RedactedLog.Create();

        using (log.Logger.BeginScope(new Dictionary<string, object?> { ["ApiKey"] = "abcd-1234" }))
        {
            log.Logger.LogInformation("Collector called");
        }

        log.Scopes.Should().NotContain(scope => scope.Contains("abcd-1234", StringComparison.Ordinal));
    }

    [Fact]
    public void RedactionCovers_AProviderRegisteredAfterTheSwap()
    {
        // The guarantee is at the factory, not at a provider, so a provider added later by a
        // package nobody has written yet is covered without anybody remembering to wrap it.
        using RedactedLog log = RedactedLog.Create(registerProviderFirst: false);

        log.Logger.LogInformation("Authenticating with {Password}", "hunter2");

        log.Single().Message.Should().NotContain("hunter2");
    }

    /// <summary>One captured log line, as a provider downstream of the redactor sees it.</summary>
    private sealed record LogLine(string Message, IReadOnlyList<KeyValuePair<string, object?>> Values)
    {
        public object? Property(string name) =>
            Values.FirstOrDefault(value => value.Key == name).Value;
    }

    /// <summary>A logging pipeline with redaction in it and a capturing provider at the end.</summary>
    private sealed class RedactedLog(ServiceProvider services, CapturingProvider provider) : IDisposable
    {
        public ILogger Logger { get; } = services.GetRequiredService<ILoggerFactory>().CreateLogger("Test");

        public IReadOnlyList<string> Scopes => provider.Scopes;

        public static RedactedLog Create(bool registerProviderFirst = true)
        {
            ServiceCollection services = new();
            CapturingProvider provider = new();

            if (registerProviderFirst)
            {
                services.AddLogging(logging => logging.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
                services.AddSecretRedaction();
            }
            else
            {
                services.AddSecretRedaction();
                services.AddLogging(logging => logging.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
            }

            return new RedactedLog(services.BuildServiceProvider(), provider);
        }

        public LogLine Single() => provider.Lines.Should().ContainSingle().Subject;

        public void Dispose() => services.Dispose();
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly List<LogLine> _lines = [];
        private readonly List<string> _scopes = [];

        public IReadOnlyList<LogLine> Lines => _lines;

        public IReadOnlyList<string> Scopes => _scopes;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines, _scopes);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<LogLine> lines, List<string> scopes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                scopes.Add(state.ToString() ?? string.Empty);
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lines.Add(new LogLine(
                    formatter(state, exception),
                    state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
            }
        }
    }
}
