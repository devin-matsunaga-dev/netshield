// Development orchestration only; never deployed (ARCHITECTURE.md §2).
// PostgreSQL + TimescaleDB, Redis, MailHog, and the service resources are
// wired in WP-0.2.
IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.Build().Run();
