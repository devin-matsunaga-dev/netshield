using FluentValidation;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;

using NetShield.Identity.Authentication;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Passwords;
using NetShield.Identity.Seeding;

using NetShield.Platform.Results;

namespace NetShield.Identity;

/// <summary>
/// Registers the Identity module: password hashing, the password policy, cookie authentication,
/// the authentication handlers, and the first-run administrator seeder.
/// </summary>
/// <remarks>
/// The <c>IdentityDbContext</c> is registered by the composition root, not here, because only the
/// composition root knows where the database is (SPEC.md §5).
/// </remarks>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything the Identity module needs, and maps nothing. The host must also call
    /// <c>AddNetShieldAuthorization()</c>; the endpoints here rely on its policies.
    /// </summary>
    public static TBuilder AddNetShieldIdentity<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<PasswordHashingOptions>()
            .Bind(builder.Configuration.GetSection(PasswordHashingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<PasswordPolicyOptions>()
            .Bind(builder.Configuration.GetSection(PasswordPolicyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<SessionOptions>()
            .Bind(builder.Configuration.GetSection(SessionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<AdministratorSeedOptions>()
            .Bind(builder.Configuration.GetSection(AdministratorSeedOptions.SectionName));

        builder.Services.TryAddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        builder.Services.TryAddSingleton<DecoyPasswordHash>();
        builder.Services.TryAddSingleton<PasswordPolicy>();

        builder.Services.TryAddScoped<SessionService>();
        builder.Services.TryAddScoped<LoginHandler>();
        builder.Services.TryAddScoped<RefreshSessionHandler>();
        builder.Services.TryAddScoped<LogoutHandler>();
        builder.Services.TryAddScoped<ChangePasswordHandler>();
        builder.Services.TryAddScoped<CurrentUserHandler>();

        builder.Services.TryAddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        builder.Services.TryAddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();

        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, IdentitySerializerContext.Default));

        builder.Services.AddHostedService<FirstRunAdministratorSeeder>();

        AddSessionAuthentication(builder.Services);

        return builder;
    }

    /// <summary>
    /// Cookie authentication, with the three flags WP-0.4 and ARCHITECTURE.md §8 require set here
    /// and nowhere else.
    /// </summary>
    /// <remarks>
    /// The cookie handler's redirect behaviour is replaced outright. Its default is to answer an
    /// unauthenticated request with <c>302</c> to a sign-in page, which is right for a
    /// server-rendered site and wrong for an API whose only client reads status codes
    /// (CONVENTIONS.md §4).
    /// </remarks>
    private static void AddSessionAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = SessionCookies.SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";

                // The refresh token is what extends a session. A sliding cookie would extend it
                // too, and would do so without the rotation that makes a stolen one detectable.
                options.SlidingExpiration = false;

                options.Events.OnRedirectToLogin = context => WriteProblemAsync(
                    context.HttpContext,
                    Error.Unauthenticated("identity.no-session", "You are not signed in."));

                options.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(
                    context.HttpContext,
                    Error.Forbidden("identity.forbidden", "You do not have permission to do that."));
            });

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<SessionOptions>>((cookie, session) =>
                cookie.ExpireTimeSpan = session.Value.SessionLifetime);

        // Authorization itself is not registered here. The policies, the role-to-permission map
        // and the two requirements every authenticated endpoint carries belong to
        // NetShield.Platform, and the composition root adds them with AddNetShieldAuthorization()
        // — one place, for every module, rather than whichever module happened to ask first.
    }

    private static Task WriteProblemAsync(HttpContext context, Error error) =>
        Result.Failure(error).ToHttpResult().ExecuteAsync(context);
}
