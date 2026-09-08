namespace NetShield.Inventory.Clients;

/// <summary>
/// The column widths and the bounds the client tables and their handlers agree on, in one place
/// so that a value the API accepts cannot be one the database refuses.
/// </summary>
internal static class ClientLimits
{
    /// <summary>The longest hostname a client can carry. The same width <c>devices</c> uses.</summary>
    internal const int HostnameLength = 255;

    /// <summary>
    /// The most bindings the resolver keeps in one cached address history.
    /// </summary>
    /// <remarks>
    /// A bound rather than a completeness guarantee. A resolution for an instant older than the
    /// oldest cached binding falls through to the database and gets the true answer, so this only
    /// decides how much of an address's past is answered without a query — and an address that
    /// has changed hands more than this many times recently is a DHCP pool, whose recent history
    /// is what anything asks about.
    /// </remarks>
    internal const int CachedBindings = 32;
}
