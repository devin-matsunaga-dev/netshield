using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace NetShield.Platform.Logging;

/// <summary>
/// A log line's structured state with its secrets removed, standing in for the state the caller
/// passed. It keeps the <c>{OriginalFormat}</c> entry so that structured sinks still group by
/// message template, and renders the message itself from the redacted values — rendering from
/// the originals would put back exactly what was just taken out.
/// </summary>
internal sealed class RedactedLogValues : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>The key Microsoft.Extensions.Logging uses for the message template.</summary>
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly IReadOnlyList<KeyValuePair<string, object?>> _values;
    private readonly SecretRedactor _redactor;
    private readonly string? _template;
    private string? _rendered;

    private RedactedLogValues(
        IReadOnlyList<KeyValuePair<string, object?>> values,
        string? template,
        SecretRedactor redactor)
    {
        _values = values;
        _template = template;
        _redactor = redactor;
    }

    public int Count => _values.Count;

    public KeyValuePair<string, object?> this[int index] => _values[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Produces a redacted stand-in, or reports that there was nothing to redact. Returning
    /// <see langword="false"/> matters: an untouched line is forwarded as the exact state the
    /// caller built, so its formatting, alignment and format specifiers are preserved and only
    /// a line that actually held a secret pays anything at all.
    /// </summary>
    internal static bool TryRedact(
        IReadOnlyList<KeyValuePair<string, object?>> values,
        SecretRedactor redactor,
        [NotNullWhen(true)] out RedactedLogValues? redacted)
    {
        redacted = null;

        KeyValuePair<string, object?>[] safe = new KeyValuePair<string, object?>[values.Count];
        string? template = null;
        bool changed = false;

        for (int index = 0; index < values.Count; index++)
        {
            KeyValuePair<string, object?> entry = values[index];

            if (entry.Key == OriginalFormatKey)
            {
                string original = entry.Value as string ?? string.Empty;
                template = redactor.RedactText(original);
                changed |= !ReferenceEquals(template, original);
                safe[index] = new KeyValuePair<string, object?>(entry.Key, template);
                continue;
            }

            object? value = redactor.RedactValue(entry.Key, entry.Value);
            changed |= !Equals(value, entry.Value);
            safe[index] = new KeyValuePair<string, object?>(entry.Key, value);
        }

        if (!changed)
        {
            return false;
        }

        redacted = new RedactedLogValues(safe, template, redactor);
        return true;
    }

    /// <summary>The rendered message, built from the redacted values.</summary>
    public override string ToString() => _rendered ??= Render();

    private string Render()
    {
        if (_template is null)
        {
            return string.Join(" ", _values.Select(entry => $"{entry.Key}={Format(entry.Value)}"));
        }

        StringBuilder message = new(_template.Length + 32);

        for (int index = 0; index < _template.Length; index++)
        {
            char character = _template[index];

            if (character is '{' or '}' && index + 1 < _template.Length && _template[index + 1] == character)
            {
                // "{{" and "}}" are how a template escapes a brace.
                message.Append(character);
                index++;
                continue;
            }

            if (character != '{')
            {
                message.Append(character);
                continue;
            }

            int close = _template.IndexOf('}', index + 1);

            if (close < 0)
            {
                message.Append(_template, index, _template.Length - index);
                break;
            }

            string hole = _template[(index + 1)..close];
            message.Append(Substitute(hole));
            index = close;
        }

        // The template itself may have carried a secret shape that no property accounts for.
        return _redactor.RedactText(message.ToString());
    }

    /// <summary>
    /// Replaces one <c>{Name}</c> hole. Alignment and format specifiers are dropped rather than
    /// honoured: this path runs only for a line that held a secret, where the structured values
    /// are what a reader will trust anyway.
    /// </summary>
    private string Substitute(string hole)
    {
        string name = hole.TrimStart('@', '$');
        int specifier = name.IndexOfAny([',', ':']);

        if (specifier >= 0)
        {
            name = name[..specifier];
        }

        foreach (KeyValuePair<string, object?> entry in _values)
        {
            if (entry.Key == name)
            {
                return Format(entry.Value);
            }
        }

        return $"{{{hole}}}";
    }

    private static string Format(object? value) => value?.ToString() ?? "(null)";
}
