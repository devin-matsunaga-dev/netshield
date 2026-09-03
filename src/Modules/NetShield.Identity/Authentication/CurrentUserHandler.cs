using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;

using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.Platform.Results;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Answers "who am I" for the session cookie on the request.
/// </summary>
/// <remarks>
/// Read from the database rather than assembled from claims. The cookie was minted when the
/// session started and cannot know that the account has since been disabled, renamed, or told to
/// change its password — and the client uses exactly those fields to decide what to show.
/// </remarks>
internal sealed class CurrentUserHandler(IdentityDbContext database)
{
    internal async Task<Result<AuthenticatedUser>> HandleAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return AuthenticationErrors.NoSession;
        }

        User? user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId.Value, cancellationToken);

        if (user is not { IsActive: true })
        {
            return AuthenticationErrors.NoSession;
        }

        return SessionService.ToAuthenticatedUser(user);
    }
}
