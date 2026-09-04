using System.Net;

namespace NetShield.Inventory.Devices;

/// <summary>
/// The bounds the validators enforce and the column widths the mapping declares, in one place so
/// the two cannot drift into a request the API accepts and the database refuses.
/// </summary>
internal static class DeviceLimits
{
    /// <summary>The longest hostname. DNS allows 253; the column allows 255.</summary>
    internal const int HostnameLength = 255;

    /// <summary>The longest model, OS version, serial, site or owner.</summary>
    internal const int AttributeLength = 128;

    /// <summary>The longest free-text note.</summary>
    internal const int NotesLength = 4000;

    /// <summary>
    /// Whether the text is an address the <c>inet</c> column can hold. <see cref="IPAddress"/>
    /// is the same parser the handler uses, so nothing can pass validation and then fail to
    /// parse.
    /// </summary>
    internal static bool IsAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value) && IPAddress.TryParse(value, out _);
}
