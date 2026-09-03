using System.Text.RegularExpressions;

namespace NetShield.Platform.Logging;

/// <summary>
/// Decides what in a log line is a secret and removes it. ARCHITECTURE.md §8 puts redaction at
/// the sink rather than in every call site, because "nobody logs a password" is a policy that
/// holds until the first time somebody does.
/// </summary>
/// <remarks>
/// Two rules, applied together. A structured property whose <em>name</em> looks like a secret
/// loses its value whatever that value is; and any <em>text</em> that carries a recognisable
/// secret shape — a <c>key=value</c> pair, a bearer token, a PEM private key — loses that part.
/// The name rule is the one that matters: it catches a secret whose value looks like nothing
/// in particular, which is most of them.
/// </remarks>
public sealed partial class SecretRedactor
{
    /// <summary>What replaces a redacted value. Fixed, so that it is greppable in a log.</summary>
    public const string Placeholder = "[REDACTED]";

    /// <summary>Whether a structured property of this name may never carry its value to a sink.</summary>
    public bool IsSecretName(string propertyName) =>
        !string.IsNullOrEmpty(propertyName) && SecretName().IsMatch(propertyName);

    /// <summary>
    /// Removes recognisable secret shapes from free text. Returns the same reference when there
    /// was nothing to remove, so a caller can tell whether the line changed without comparing it.
    /// </summary>
    public string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string redacted = KeyedSecret().Replace(text, match =>
            $"{match.Groups["key"].Value}{match.Groups["separator"].Value}{Placeholder}");

        redacted = BearerToken().Replace(redacted, $"Bearer {Placeholder}");
        redacted = PrivateKeyBlock().Replace(redacted, Placeholder);

        return string.Equals(redacted, text, StringComparison.Ordinal) ? text : redacted;
    }

    /// <summary>
    /// Redacts one structured property. The name rule wins outright; otherwise the value's text
    /// form is scanned, which is what catches a connection string or a token pasted into a
    /// message that nobody thought of as secret.
    /// </summary>
    public object? RedactValue(string propertyName, object? value)
    {
        if (IsSecretName(propertyName))
        {
            return Placeholder;
        }

        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            string safe = RedactText(text);
            return ReferenceEquals(safe, text) ? value : safe;
        }

        // Numbers, booleans, chars and dates cannot hold a secret shape, and rendering every one
        // of them on every log line is the kind of cost a logging path does not get to have.
        if (Type.GetTypeCode(value.GetType()) != TypeCode.Object)
        {
            return value;
        }

        if (value.ToString() is not { } rendered)
        {
            return value;
        }

        string safeRendered = RedactText(rendered);
        return ReferenceEquals(safeRendered, rendered) ? value : safeRendered;
    }

    /// <summary>
    /// Property names that carry a secret. Deliberately greedy: a false redaction costs a
    /// debugging session, a missed one costs a credential (SPEC.md §5).
    /// </summary>
    [GeneratedRegex(
        """(pass(word|wd|phrase)|pwd|secret|token|api[-_]?key|credential|community|private[-_]?key|authorization|cookie|session[-_]?id|bearer|kek|dek)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    /// <summary>
    /// A secret introduced by its own name inside free text, as a config line, a query string or
    /// a connection string does. The name and separator are kept so the line still reads.
    /// </summary>
    [GeneratedRegex(
        """(?<key>\b(pass(word|wd|phrase)|pwd|secret|token|api[-_]?key|community|credential)\b)(?<separator>\s*[=:]\s*)("[^"]*"|'[^']*'|[^\s;,&"']+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyedSecret();

    /// <summary>An HTTP bearer credential, wherever it was rendered from.</summary>
    [GeneratedRegex(
        """\bBearer\s+[A-Za-z0-9\-._~+/]+=*""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    /// <summary>A PEM private key, which SSH credential handling makes a live risk from WP-1.2 on.</summary>
    [GeneratedRegex(
        """-----BEGIN[^-]*PRIVATE KEY-----[\s\S]*?-----END[^-]*PRIVATE KEY-----""",
        RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyBlock();
}
