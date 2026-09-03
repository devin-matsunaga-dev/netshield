using NetShield.Contracts.Identity;

using NetShield.Platform.Results;

namespace NetShield.Platform.Authorization;

/// <summary>
/// The module-level half of ARCHITECTURE.md §8: the check a handler makes for itself, after the
/// endpoint has already made one.
/// </summary>
/// <remarks>
/// <para>
/// The two checks are not redundant. The endpoint check answers "may this role call this route";
/// this one answers "may this actor do this to this thing", and it is the one that keeps holding
/// when a handler is later reached from a second route, from a batch operation, or from a
/// background job that has no endpoint at all.
/// </para>
/// <para>
/// It returns a <see cref="Result"/> rather than throwing, because CONVENTIONS.md §2 keeps
/// exceptions for bugs, and a refusal is an expected answer that the endpoint layer already
/// knows how to turn into a 403.
/// </para>
/// </remarks>
public interface IResourceGuard
{
    /// <summary>
    /// Succeeds when the caller may do <paramref name="permission"/> to the named resource, and
    /// otherwise returns the failure the endpoint layer maps to 401 or 403.
    /// </summary>
    /// <param name="permission">The capability the operation needs.</param>
    /// <param name="resourceType">What is being acted on, for the audit row — e.g. <c>device</c>.</param>
    /// <param name="resourceId">Which one, when the operation names one.</param>
    Result Require(Permission permission, string resourceType, string? resourceId = null);
}
