// NetShield.Ingest hosts the push-based syslog and NetFlow/IPFIX receivers
// (ARCHITECTURE.md §2 and §6). The receivers themselves are built in later
// packages; this is the host they will be registered into. ServiceDefaults gives it
// OpenTelemetry and health checks from the start, so the Aspire dashboard models
// every runtime process rather than only the API.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

IHost host = builder.Build();
host.Run();
