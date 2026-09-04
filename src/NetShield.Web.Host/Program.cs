// NetShield.Web.Host is the composition root: the API, RBAC, and SPA hosting
// (ARCHITECTURE.md §2 and §4). Endpoint mapping and module registration arrive in
// later packages; this wires the host to the infrastructure Aspire supplies.
using NetShield.Identity;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Persistence;

using NetShield.Inventory;
using NetShield.Inventory.Endpoints;
using NetShield.Inventory.Persistence;

using NetShield.Platform;
using NetShield.Platform.Api;
using NetShield.Platform.Auditing;
using NetShield.Platform.Persistence;
using NetShield.Platform.Problems;

using NetShield.Web.Host;

// The schema step runs as this same artifact with an argument, and never binds a socket: an
// operator migrates before the API starts, rather than every replica racing to migrate on boot
// (ARCHITECTURE.md §2 keeps the process model at five, so this is not a sixth project).
if (MigrationMode.IsRequested(args))
{
    return await MigrationMode.RunAsync(args, CancellationToken.None);
}

// Key rotation is the same shape and for the same reasons: a privileged operation an operator
// runs deliberately, not a route that sits on the web surface waiting to be reached. It writes
// its own audit row (SPEC.md §5).
if (RewrapMode.IsRequested(args))
{
    return await RewrapMode.RunAsync(args, CancellationToken.None);
}

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

// As does the Inventory module's. It maps outbox_messages as well as its own tables, so a device
// write and the event describing it are one transaction (ARCHITECTURE.md §5).
builder.AddNpgsqlDbContext<InventoryDbContext>(
    ConnectionNames.Database,
    settings => settings.DisableHealthChecks = true,
    options => options.UseInventoryConventions());

builder.AddNetShieldPlatform();
builder.AddOutboxDispatcher();
builder.Services.AddNetShieldProblemDetails();

// RBAC and the audit log. AddNetShieldAuthorization makes the API deny by default: an endpoint
// that declares no policy is refused rather than published (ARCHITECTURE.md §8).
builder.AddNetShieldAuthorization();
builder.AddNetShieldAudit();

builder.AddNetShieldIdentity();
builder.AddNetShieldInventory();

// The OpenAPI description of every /api endpoint. CONVENTIONS.md §4 generates it from the
// endpoints and generates the TypeScript client from it; src/NetShield.Web.Host/openapi/v1.json
// is the committed copy the client is built from.
builder.AddNetShieldApiDocument();

WebApplication app = builder.Build();

// First in the pipeline: an unhandled exception must become problem details before anything
// else has a chance to render it (SPEC.md §5).
app.UseNetShieldProblemDetails();

// Before authentication, so the SPA's own assets are served without touching the auth pipeline.
// They are the shell that renders the sign-in page; there is no session yet to check.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();

// After authentication and before authorization, so that the row knows who the caller was and
// still gets written when authorization refuses them.
app.UseNetShieldAudit();

app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapNetShieldApiDocument();
app.MapIdentityEndpoints();
app.MapInventoryEndpoints();

// Last: every path the API did not claim is a client-side route.
app.MapNetShieldSpa();

app.Run();

return 0;
