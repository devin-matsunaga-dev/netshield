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

// The API. /health/ready covers PostgreSQL and Redis, so the dashboard reports this
// resource healthy only once the stores it depends on are actually reachable.
builder.AddProject<Projects.NetShield_Web_Host>("web-host")
    .WithReference(database).WaitFor(database)
    .WithReference(cache).WaitFor(cache)
    .WithReference(mailConnection).WaitFor(mail)
    .WithHttpHealthCheck("/health/ready");

// The push-ingest worker. Registered so that aspire run models every runtime process
// and its traces reach the dashboard. The syslog and flow receivers, and any store it
// needs, arrive with their own packages in Phases 4 and 5.
builder.AddProject<Projects.NetShield_Ingest>("ingest");

builder.Build().Run();
