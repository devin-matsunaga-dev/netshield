namespace NetShield.Inventory.Devices;

/// <summary>
/// Normalises the free-form labels a caller sends, so that <c>Core</c>, <c>core</c> and
/// <c> core </c> are one tag and a filter for <c>core</c> finds all three.
/// </summary>
internal static class DeviceTags
{
    /// <summary>The longest a single tag may be.</summary>
    internal const int MaximumLength = 32;

    /// <summary>The most tags one device may carry.</summary>
    internal const int MaximumCount = 24;

    /// <summary>
    /// Trims, lower-cases, drops empties and duplicates, and sorts. Sorting is what makes two
    /// devices tagged the same store the same array, so a diff of an audit snapshot is about the
    /// tags rather than about the order they were typed in.
    /// </summary>
    internal static IReadOnlyList<string> Normalize(IReadOnlyList<string>? tags) =>
        tags is null
            ? []
            : [.. tags
                .Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
}
