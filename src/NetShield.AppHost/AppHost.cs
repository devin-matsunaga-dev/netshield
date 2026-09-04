// Development orchestration only; never deployed (ARCHITECTURE.md §2). The deployment
// orchestrator is Docker Compose and it is not this file's concern.
//
// Every connection string in NetShield originates here and reaches a service as an
// Aspire reference. Nothing downstream carries a host, a port, or a credential in
// configuration (SPEC.md §5, CONVENTIONS.md §8).
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 17 with TimescaleDB — the single store for relational and time-series
// data alike (ARCHITECTURE.md §3). The image's own init script installs the extension
// into template1, so every database created from it inherits timescaledb.
// TIMESCALEDB_TELEMETRY=off both sets timescaledb.telemetry_level=off and cancels the
// background telemetry job: ARCHITECTURE.md §1 permits no outbound internet call at
// runtime, and the image reports home on a schedule unless told not to.
IResourceBuilder<PostgresServerResource> postgres = builder
    .AddPostgres("postgres")
    .WithImage("timescale/timescaledb", "2.29.0-pg17")
    .WithEnvironment("TIMESCALEDB_TELEMETRY", "off")
    .WithDataVolume("netshield-postgres-data");

IResourceBuilder<PostgresDatabaseResource> database = postgres.AddDatabase("netshield");

// Redis is cache, rate limiting, job coordination and the SignalR backplane, and is
// never a source of truth (ARCHITECTURE.md §3). Snapshotting is on so the data volume
// survives a restart as a warm cache; a full flush must still cost nothing but latency.
IResourceBuilder<RedisResource> cache = builder
    .AddRedis("cache")
    .WithDataVolume("netshield-redis-data")
    .WithPersistence();

// Local SMTP sink for notification channels (SPEC.md §2, Alerting). Nothing sends mail
// until Phase 6; this stands the resource up and publishes its connection string.
IResourceBuilder<ContainerResource> mail = builder
    .AddContainer("mailpit", "axllent/mailpit", "v1.31.0")
    .WithEndpoint(targetPort: 1025, scheme: "tcp", name: "smtp")
    .WithHttpEndpoint(targetPort: 8025, name: "http")
    .WithEnvironment("MP_DATABASE", "/data/mailpit.db")
    .WithVolume("netshield-mailpit-data", "/data")
    .WithHttpHealthCheck("/readyz", endpointName: "http");

EndpointReference smtp = mail.GetEndpoint("smtp");

IResourceBuilder<IResourceWithConnectionString> mailConnection = builder.AddConnectionString(
    "mail",
    ReferenceExpression.Create(
        $"smtp://{smtp.Property(EndpointProperty.Host)}:{smtp.Property(EndpointProperty.Port)}"));

// The first-run administrator's initial password. Aspire generates it on first run and
// persists it to the AppHost's user-secrets store, so it survives a restart and the
// account it created still opens with it. The operator reads it once from the dashboard's
// parameter list and is made to change it at first sign-in. It is never written into this
// repository and never logged (SPEC.md §5).
IResourceBuilder<ParameterResource> administratorPassword = builder.AddParameter(
    "identity-admin-password",
    new GenerateParameterDefault
    {
        MinLength = 20,
        MinLower = 1,
        MinUpper = 1,
        MinNumeric = 1,
        MinSpecial = 1
    },
    secret: true,
    persist: true);

// The schema step. It is the same project as the API, run with --migrate: it applies every
// context's migrations, seeds the first-run administrator, and exits without binding a socket.
// A sixth project would be a change to the five-process model in ARCHITECTURE.md §2; a startup
// hook would have every replica racing to migrate and would leave the API's account holding DDL
// rights for its whole life. Deployment runs the same image with the same argument.
IResourceBuilder<ProjectResource> migrator = builder.AddProject<Projects.NetShield_Web_Host>("db-migrator")
    .WithArgs("--migrate")
    .WithReference(database).WaitFor(database)
    .WithEnvironment("Identity__Seed__Password", administratorPassword);

// The API. /health/ready covers PostgreSQL and Redis, so the dashboard reports this
// resource healthy only once the stores it depends on are actually reachable.
IResourceBuilder<ProjectResource> webHost = builder.AddProject<Projects.NetShield_Web_Host>("web-host")
    .WithReference(database).WaitFor(database)
    .WithReference(cache).WaitFor(cache)
    .WithReference(mailConnection).WaitFor(mail)
    .WithEnvironment("Identity__Seed__Password", administratorPassword)
    .WithHttpHealthCheck("/health/ready")
    // Not merely started: finished. The API must never come up against a half-migrated schema,
    // and on a fresh volume the administrator it seeds is created by the step above.
    .WaitForCompletion(migrator);

// The SPA, under the Vite dev server. In deployment Web.Host serves the built bundle out of its
// own wwwroot and this resource does not exist; in development the dev server owns the page and
// proxies /api to the API, which is the only way it learns the address (SPEC.md §5). npm install
// runs first, so a fresh clone comes up with one command.
//
// The address is passed twice, on purpose. WithReference publishes it as Aspire's own
// service-discovery variable, `services__web-host__http__0` — and that name can never reach the
// dev server. `npm run dev` runs its script through `sh -c`, and a POSIX shell exports only
// names that are valid shell identifiers, so it silently drops every variable whose name carries
// the hyphen in "web-host". The dev server then configures no proxy and answers /api with
// index.html, which the SPA reads as "Unexpected token '<'". NETSHIELD_API_URL carries the same
// value under a name a shell will pass on. The reference stays for the dashboard's dependency
// graph and for whatever reads it in a non-shell launcher.
builder.AddViteApp("web-client", "../NetShield.Web.Client")
    .WithNpm()
    .WithReference(webHost)
    .WithEnvironment("NETSHIELD_API_URL", webHost.GetEndpoint("http"))
    .WaitFor(webHost);

// The push-ingest worker. Registered so that aspire run models every runtime process
// and its traces reach the dashboard. The syslog and flow receivers, and any store it
// needs, arrive with their own packages in Phases 4 and 5.
builder.AddProject<Projects.NetShield_Ingest>("ingest");

builder.Build().Run();
