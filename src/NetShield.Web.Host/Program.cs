// NetShield.Web.Host is the composition root: the API, RBAC, and SPA hosting
// (ARCHITECTURE.md §2 and §4). Endpoint mapping and module registration arrive in
// later packages; this wires the host to the infrastructure Aspire supplies.
using NetShield.Identity;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Persistence;

using NetShield.Platform;
using NetShield.Platform.Persistence;
using NetShield.Platform.Problems;

using NetShield.Web.Host;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Connection names come from NetShield.AppHost. Neither the host, the port, nor the
// credential appears in configuration here (SPEC.md §5). Both registrations contribute
// a readiness health check, which is what /health/ready reports on.
builder.AddNpgsqlDbContext<PlatformDbContext>(
    ConnectionNames.Database,
    configureDbContextOptions: options => options.UseNetShieldConventions());
builder.AddRedisClient(ConnectionNames.Cache);

// The Identity module's tables live in the same database. Its readiness is already covered
// by the context above, so this one contributes no second check on the same connection.
builder.AddNpgsqlDbContext<IdentityDbContext>(
    ConnectionNames.Database,
    settings => settings.DisableHealthChecks = true,
    options => options.UseIdentityConventions());

builder.AddNetShieldPlatform();
builder.AddOutboxDispatcher();
builder.Services.AddNetShieldProblemDetails();

builder.AddNetShieldIdentity();

WebApplication app = builder.Build();

// First in the pipeline: an unhandled exception must become problem details before anything
// else has a chance to render it (SPEC.md §5).
app.UseNetShieldProblemDetails();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapIdentityEndpoints();

app.Run();
