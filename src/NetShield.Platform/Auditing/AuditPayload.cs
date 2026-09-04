using System.Text.Json;

using NetShield.Platform.Logging;

namespace NetShield.Platform.Auditing;

/// <summary>
/// Turns a before/after snapshot into the <c>jsonb</c> an audit row stores, with the same
/// redaction the log sink applies.
/// </summary>
/// <remarks>
/// SPEC.md §5 covers the database as well as the log, and an append-only table is the one place
/// a leaked secret can never be taken back out. Redaction here is by property name through
/// <see cref="SecretRedactor"/>, so a handler that puts a password in a snapshot by mistake
/// stores <c>[REDACTED]</c> rather than the password.
///
/// Public since WP-1.2. <c>AuditMiddleware</c> is not the only writer any more: a command that
/// runs outside the request pipeline — key rotation — appends its own row, and it has to redact
/// what it records exactly the way every other row is redacted. A second serialiser written
/// beside this one is how the two would come to disagree.
/// </remarks>
public static class AuditPayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The redacted JSON for <paramref name="state"/>, or <see langword="null"/> when there is
    /// nothing to record — a null column reads better than an empty object.
    /// </summary>
    public static string? Serialize(IReadOnlyDictionary<string, object?>? state, SecretRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);

        if (state is null || state.Count == 0)
        {
            return null;
        }

        Dictionary<string, object?> safe = new(state.Count, StringComparer.Ordinal);

        foreach ((string name, object? value) in state)
        {
            safe[name] = redactor.RedactValue(name, value);
        }

        return JsonSerializer.Serialize(safe, Options);
    }
}
