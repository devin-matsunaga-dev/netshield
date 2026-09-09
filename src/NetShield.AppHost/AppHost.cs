// Development orchestration only; never deployed (ARCHITECTURE.md §2). The deployment
// orchestrator is Docker Compose and it is not this file's concern.
//
// Every connection string in NetShield originates here and reaches a service as an
// Aspire reference. Nothing downstream carries a host, a port, or a credential in
// configuration (SPEC.md §5, CONVENTIONS.md §8).
using Aspire.Hosting.ApplicationModel;

using NetShield.AppHost;

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

// The key-encryption key that wraps every stored device credential (ARCHITECTURE.md §8).
// Generated once as 32 random bytes, base64-encoded, and persisted to the AppHost's user-secrets
// store — a KEK that changed between runs would leave every credential in the development
// database permanently unreadable. It is never written into this repository and never logged
// (SPEC.md §5).
//
// This reaches development and nothing reaches a deployment. A deployment must supply the same
// configuration key from a secret store, a mounted file, or a KMS, held somewhere other than
// beside the database whose rows it opens. That gap is recorded in STATUS.md, not papered over
// here.
IResourceBuilder<ParameterResource> credentialKey = builder.AddParameter(
    "credential-kek",
    new Base64KeyParameterDefault(32),
    secret: true,
    persist: true);

// The shared secret netshield-collector presents on /internal/collector (ARCHITECTURE.md §7).
// Generated once and persisted to the AppHost's user-secrets store, so the collector and the API
// still agree after a restart. Unlike the key-encryption key above, this one is a bearer token
// rather than a key: its only requirement is that it is long and unguessable, so Aspire's own
// generator is exactly right for it and no custom default is needed.
//
// A deployment supplies this the same way it supplies the other two — from a secret store, a
// mounted file, or a KMS — and gives the same value to the API and to every collector.
IResourceBuilder<ParameterResource> collectorSecret = builder.AddParameter(
    "collector-shared-secret",
    new GenerateParameterDefault
    {
        MinLength = 48,
        MinLower = 1,
        MinUpper = 1,
        MinNumeric = 1,
        MinSpecial = 0
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
//
// **Its endpoints are not proxied, and that is what makes `aspire run` work on its own.**
// DCP proxies every resource endpoint by default, and the build shipped with Aspire 13.5.3
// intermittently never wires an upstream to one of them: the proxy accepts the TCP connection
// and never dials the API, which is answering 200 Healthy on its own port throughout. It picks
// a different endpoint to break on each run — an earlier session watched it take the http one
// and this one watched it take the https one — and 13.4.6's DCP does it too, so the version
// pin that used to be documented here was never a fix.
//
// The damage came through two seams, both of which ran through the proxy. `WithHttpHealthCheck`
// probes the endpoint URL, so a dead proxy left `web-host` for ever unhealthy and the two
// resources that `WaitFor` it never started at all. And `GetEndpoint("http")` below hands that
// same URL to the collector and the dev server, so even when they did start they could not
// reach the API. Unproxied, the endpoint URL *is* the address the API is listening on, and both
// seams are fixed by the one change.
//
// `launchProfileName: null` is the half of it that was missing when this was tried before.
// `Properties/launchSettings.json` pins 7235 and 5295; Aspire takes those as the *endpoint*
// ports and gives the application random target ports behind them. Turning the proxy off while
// the profile still pinned 5295 handed the API the port DCP had already bound, and it died with
// "address already in use" — which read as the approach failing when it was the launch profile
// that had to go. Without the profile, Aspire allocates the ports and the API binds them itself.
IResourceBuilder<ProjectResource> webHost = builder
    .AddProject<Projects.NetShield_Web_Host>("web-host", launchProfileName: null)
    .WithHttpEndpoint(name: "http", isProxied: false)
    .WithHttpsEndpoint(name: "https", isProxied: false)
    // The launch profile carried two things and the endpoints above replace only one of them.
    // The other is the environment name, and without it the API comes up in Production: the
    // health endpoints are mapped only in development (ServiceDefaults), so `/health/ready`
    // fell through to the SPA fallback and answered 200 with index.html — a health check that
    // passes for the wrong reason, which is worse than one that fails. Taken from the AppHost's
    // own environment rather than hard-coded, which is what the profile was doing.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(database).WaitFor(database)
    .WithReference(cache).WaitFor(cache)
    .WithReference(mailConnection).WaitFor(mail)
    .WithEnvironment("Identity__Seed__Password", administratorPassword)
    // The key ring. Only the API gets it: db-migrator applies schema and encrypts nothing, and a
    // process is not handed the highest-value secret in the system for a job that has no use for
    // it. The key id is "dev" and stays "dev" — a rotation adds a second key and moves
    // ActiveKeyId to it, which is what NetShield.Web.Host --rewrap then walks the table for.
    .WithEnvironment("Security__CredentialEncryption__ActiveKeyId", "dev")
    .WithEnvironment("Security__CredentialEncryption__Keys__dev", credentialKey)
    // The other half of the collector contract. The API holds the secret it will compare against;
    // db-migrator does not get it either, for the same reason it does not get the key ring.
    .WithEnvironment("Collector__SharedSecret", collectorSecret)
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
// Its endpoint is unproxied for the reason the API's is: the page is what a person opens, and
// a dead proxy in front of it is a blank screen with no error on it.
builder.AddViteApp("web-client", "../NetShield.Web.Client")
    .WithEndpoint("http", endpoint => endpoint.IsProxied = false)
    .WithNpm()
    .WithReference(webHost)
    .WithEnvironment("NETSHIELD_API_URL", webHost.GetEndpoint("http"))
    .WaitFor(webHost);

// The pull-collection worker (ARCHITECTURE.md §2). uv owns the environment, so the resource runs
// `uv sync` before `python -m collector` — a fresh clone comes up with one command, the same
// promise npm install makes for the SPA above.
//
// It is handed three things and no more: where the API is, the shared secret, and what to call
// itself. It holds no database credential and never touches PostgreSQL (ARCHITECTURE.md §7);
// there is no connection string reference here to make that a rule rather than an omission.
// Device credentials reach it per job, in the lease response, and are never written to its disk.
builder.AddPythonModule("collector", "../netshield-collector", "collector")
    .WithUv()
    .WithEnvironment("NETSHIELD_API_URL", webHost.GetEndpoint("http"))
    .WithEnvironment("NETSHIELD_COLLECTOR_SECRET", collectorSecret)
    .WithEnvironment("NETSHIELD_COLLECTOR_NAME", "collector-dev")
    .WaitFor(webHost);

// The push-ingest worker. Registered so that aspire run models every runtime process
// and its traces reach the dashboard. The syslog and flow receivers, and any store it
// needs, arrive with their own packages in Phases 4 and 5.
builder.AddProject<Projects.NetShield_Ingest>("ingest");

builder.Build().Run();
