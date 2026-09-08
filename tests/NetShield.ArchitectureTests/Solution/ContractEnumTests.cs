using System.Reflection;
using System.Text.Json.Serialization;

using FluentAssertions;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// Every enum in <c>NetShield.Contracts</c> travels as its name, not as its ordinal.
/// </summary>
/// <remarks>
/// <para>
/// WP-0.4 settled that an enum is written as its name so that inserting a member cannot change
/// what a stored response, a generated client or a saved fixture already meant. Five inventory
/// enums then spent six packages on the wire as integers anyway, because the converters were
/// named in a serializer context's <c>JsonSourceGenerationOptions</c> rather than on the types —
/// and that list has no effect once the context is one resolver among several on the host's JSON
/// options. The documentation on <c>DeviceVendor</c> claimed name serialisation the whole time.
/// </para>
/// <para>
/// This is the rule that makes the claim checkable. It is deliberately about the attribute's
/// <em>placement</em>, because placement is the thing that was wrong: an attribute on the type
/// holds wherever the type is serialised, and nothing else does.
/// </para>
/// </remarks>
public sealed class ContractEnumTests
{
    /// <summary>
    /// Enums a client would have to guess the meaning of an integer for. Everything public in
    /// <c>Contracts</c> is on the wire by construction — the assembly exists to be the shapes
    /// that cross a boundary (ARCHITECTURE.md §4), and it references nothing.
    /// </summary>
    private static IReadOnlyList<Type> ContractEnums { get; } =
    [
        .. typeof(NetShield.Contracts.Inventory.DeviceState).Assembly
            .GetExportedTypes()
            .Where(type => type.IsEnum)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
    ];

    [Fact]
    public void ContractEnums_AreDiscovered()
    {
        // A rule that silently found nothing to check would pass for ever. WP-1.7's own five are
        // among these, and the count only ever grows.
        ContractEnums.Should().HaveCountGreaterThan(10);
    }

    /// <summary>
    /// The attribute has to be on the enum itself. <c>Inherit = false</c> is the point of the
    /// check rather than an incidental argument to it.
    /// </summary>
    [Fact]
    public void EveryContractEnum_CarriesAStringConverterOnTheTypeItself()
    {
        IReadOnlyList<string> offenders =
        [
            .. ContractEnums
                .Where(type => type.GetCustomAttribute<JsonConverterAttribute>(inherit: false) is null)
                .Select(type => type.FullName!)
        ];

        offenders.Should().BeEmpty(
            "an enum with no [JsonConverter] on the type is written as its ordinal, so inserting "
            + "a member silently changes what every response already captured meant "
            + "(WP-0.4; the defect this rule exists for was found in WP-1.4 and fixed in WP-1.7). "
            + "Add [JsonConverter(typeof(JsonStringEnumConverter<T>))] to the enum.");
    }

    /// <summary>
    /// And it has to be the string converter for that same enum — a converter for a different
    /// type compiles, and would write the ordinal after all.
    /// </summary>
    [Fact]
    public void EveryContractEnum_NamesTheStringConverterForItself()
    {
        IReadOnlyList<string> offenders =
        [
            .. ContractEnums
                .Select(type => (Type: type,
                    Converter: type.GetCustomAttribute<JsonConverterAttribute>(inherit: false)?.ConverterType))
                .Where(pair => pair.Converter != typeof(JsonStringEnumConverter<>).MakeGenericType(pair.Type))
                .Select(pair => $"{pair.Type.FullName} names {pair.Converter?.FullName ?? "nothing"}")
        ];

        offenders.Should().BeEmpty(
            "the converter has to be JsonStringEnumConverter<T> for the enum it sits on.");
    }
}
