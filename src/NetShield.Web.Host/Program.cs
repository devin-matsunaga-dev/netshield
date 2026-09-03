// NetShield.Web.Host is the composition root: the API, RBAC, and SPA hosting
// (ARCHITECTURE.md §2 and §4). Endpoint mapping, ServiceDefaults registration,
// and health endpoints arrive in WP-0.2 and later.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();
app.Run();
