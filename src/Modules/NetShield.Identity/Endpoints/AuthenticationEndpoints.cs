using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Contracts.Identity;

using NetShield.Identity.Authentication;

using NetShield.Platform.Results;

namespace NetShield.Identity.Endpoints;

/// <summary>
/// The local-authentication endpoints, under <c>/api/v1/auth</c> (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// The endpoint layer owns the HTTP facts and nothing else: it writes the two cookies, and it
/// turns a handler's <see cref="Result{T}"/> into a status code. Every handler here returns a
/// <c>SessionGrant</c> rather than a response body, so that the refresh token can only ever reach
/// a cookie and never a payload.
/// </remarks>
public static class AuthenticationEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/api/v1/auth";

    /// <summary>Maps the authentication endpoints. Called once, by the composition root.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix).WithTags("Authentication");

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .AllowAnonymous()
            .WithName("Login");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshSession");

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("Logout");

        group.MapPost("/password", ChangePasswordAsync)
            .AddEndpointFilter<ValidationFilter<ChangePasswordRequest>>()
            .RequireAuthorization()
            .WithName("ChangePassword");

        group.MapGet("/me", CurrentUserAsync)
            .RequireAuthorization()
            .WithName("CurrentUser");

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<SessionGrant> result = await handler.HandleAsync(request, cancellationToken);

        return await WriteSessionAsync(result, context);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshSessionHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        string? presented = context.Request.Cookies[SessionCookies.RefreshCookieName];

        Result<SessionGrant> result = await handler.HandleAsync(presented, cancellationToken);

        if (!result.IsSuccess)
        {
            // A refresh that fails leaves nothing usable behind: the cookies go, so the client
            // arrives at the sign-in page rather than retrying a token that will never work.
            await ClearSessionAsync(context);
        }

        return await WriteSessionAsync(result, context);
    }

    private static async Task<IResult> LogoutAsync(
        LogoutHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(
            SessionClaims.SessionIdOf(context.User),
            context.Request.Cookies[SessionCookies.RefreshCookieName],
            cancellationToken);

        await ClearSessionAsync(context);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ChangePasswordHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<SessionGrant> result = await handler.HandleAsync(
            SessionClaims.UserIdOf(context.User),
            request,
            cancellationToken);

        return await WriteSessionAsync(result, context);
    }

    private static async Task<IResult> CurrentUserAsync(
        CurrentUserHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<AuthenticatedUser> result = await handler.HandleAsync(
            SessionClaims.UserIdOf(context.User),
            cancellationToken);

        return result.ToHttpResult();
    }

    /// <summary>
    /// Turns a grant into the two cookies and a body describing the user, or a failure into
    /// problem details. The grant itself never reaches the response.
    /// </summary>
    private static async Task<IResult> WriteSessionAsync(Result<SessionGrant> result, HttpContext context)
    {
        if (!result.IsSuccess)
        {
            return Result<AuthenticatedUser>.Failure(result.Error).ToHttpResult();
        }

        SessionGrant grant = result.Value;

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            grant.Principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });

        SessionCookies.WriteRefresh(context.Response, grant.RefreshToken, grant.RefreshExpiresAt);

        return TypedResults.Ok(grant.User);
    }

    private static async Task ClearSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        SessionCookies.ClearRefresh(context.Response);
    }
}
