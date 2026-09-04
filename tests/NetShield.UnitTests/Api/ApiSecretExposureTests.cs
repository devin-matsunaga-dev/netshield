using System.Text.Json;

using FluentAssertions;

using NetShield.Platform.Logging;

namespace NetShield.UnitTests.Api;

/// <summary>
/// The WP-1.2 gate: the API never returns a secret value in any response shape.
/// </summary>
/// <remarks>
/// <para>
/// It is written against the committed OpenAPI document rather than against a list of types,
/// because the document is derived from the endpoints and so covers every response of every
/// package that comes after this one — including the ones nobody thought to add to a list. A
/// future endpoint that returns a shape carrying a <c>password</c>, a <c>community</c> or a
/// <c>privateKey</c> fails this test on the run that introduces it.
/// </para>
/// <para>
/// "Is this a secret" is decided by <see cref="SecretRedactor"/>, the same judgement that blanks
/// a log line and an audit snapshot. One definition, three places it is enforced.
/// </para>
/// <para>
/// Request bodies are deliberately not checked. A credential has to arrive somehow, and
/// <c>CredentialMaterial</c> exists to carry one in; write-only means it goes in and does not
/// come back, not that it has no way in.
/// </para>
/// </remarks>
public sealed class ApiSecretExposureTests
{
    private static readonly SecretRedactor Redactor = new();

    /// <summary>
    /// The one member that trips the name rule and carries no secret.
    /// </summary>
    /// <remarks>
    /// <c>AuthenticatedUser.mustChangePassword</c> is a boolean saying whether the session owes a
    /// password change. <see cref="SecretRedactor"/> blanks by name and does not stop to consider
    /// that a boolean cannot be a password — the WP-0.5 lesson, which is why that package named
    /// the equivalent audit member <c>changeRequired</c>. The response member kept the obvious
    /// name, and renaming it now would be a change to the auth contract and to the SPA that reads
    /// it, which WP-1.2 has no instruction to make. It is exempted here by name and recorded in
    /// STATUS.md so that the package that next touches the auth contract can settle it.
    ///
    /// This list is not a place to quiet a real finding. Anything added to it has to be a member
    /// that provably cannot carry a secret, with the reason written down.
    /// </remarks>
    private static readonly string[] Exempt = ["mustChangePassword"];

    [Fact]
    public async Task NoResponseShape_CarriesAMemberThatWouldBeRedacted()
    {
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(ApiDocument.CommittedFile, TestContext.Current.CancellationToken));

        JsonElement root = document.RootElement;

        IReadOnlyList<string> exposed =
        [
            .. from path in root.GetProperty("paths").EnumerateObject()
               from operation in path.Value.EnumerateObject()
               where operation.Value.ValueKind is JsonValueKind.Object
               where operation.Value.TryGetProperty("responses", out _)
               from response in operation.Value.GetProperty("responses").EnumerateObject()
               from schema in SchemasOf(response.Value)
               from member in MembersOf(schema, root, [])
               where Redactor.IsSecretName(member)
               where !Exempt.Contains(member, StringComparer.Ordinal)
               select $"{operation.Name.ToUpperInvariant()} {path.Name} -> {response.Name} carries '{member}'"
        ];

        exposed.Should().BeEmpty(
            "SPEC.md §5 and WP-1.2: a credential is write-only over the API, so no response shape "
            + "may carry a member SecretRedactor would blank");
    }

    /// <summary>The response's schema, for every content type it offers.</summary>
    private static IEnumerable<JsonElement> SchemasOf(JsonElement response)
    {
        if (!response.TryGetProperty("content", out JsonElement content))
        {
            yield break;
        }

        foreach (JsonProperty media in content.EnumerateObject())
        {
            if (media.Value.TryGetProperty("schema", out JsonElement schema))
            {
                yield return schema;
            }
        }
    }

    /// <summary>
    /// Every property name reachable from a schema, following <c>$ref</c> into
    /// <c>components/schemas</c> and descending through arrays, maps and compositions.
    /// </summary>
    /// <param name="seen">
    /// The refs already followed. The document is a graph and a shape that referred to itself
    /// would otherwise be walked forever.
    /// </param>
    private static IEnumerable<string> MembersOf(JsonElement schema, JsonElement root, HashSet<string> seen)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            yield break;
        }

        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string pointer = reference.GetString() ?? string.Empty;

            if (!seen.Add(pointer) || !TryResolve(pointer, root, out JsonElement resolved))
            {
                yield break;
            }

            foreach (string member in MembersOf(resolved, root, seen))
            {
                yield return member;
            }

            yield break;
        }

        if (schema.TryGetProperty("properties", out JsonElement properties))
        {
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                yield return property.Name;

                foreach (string member in MembersOf(property.Value, root, seen))
                {
                    yield return member;
                }
            }
        }

        foreach (string name in (string[])["items", "additionalProperties"])
        {
            if (schema.TryGetProperty(name, out JsonElement nested))
            {
                foreach (string member in MembersOf(nested, root, seen))
                {
                    yield return member;
                }
            }
        }

        foreach (string name in (string[])["allOf", "anyOf", "oneOf"])
        {
            if (!schema.TryGetProperty(name, out JsonElement composed)
                || composed.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement part in composed.EnumerateArray())
            {
                foreach (string member in MembersOf(part, root, seen))
                {
                    yield return member;
                }
            }
        }
    }

    /// <summary>Follows a local <c>#/components/schemas/Name</c> pointer.</summary>
    private static bool TryResolve(string pointer, JsonElement root, out JsonElement schema)
    {
        schema = default;

        if (!pointer.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        JsonElement current = root;

        foreach (string segment in pointer[2..].Split('/'))
        {
            if (!current.TryGetProperty(segment.Replace("~1", "/", StringComparison.Ordinal), out current))
            {
                return false;
            }
        }

        schema = current;

        return true;
    }
}
