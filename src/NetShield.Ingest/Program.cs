// NetShield.Ingest hosts the push-based syslog and NetFlow/IPFIX receivers
// (ARCHITECTURE.md §2 and §6). The receivers themselves are built in later
// packages; this is the host they will be registered into.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

IHost host = builder.Build();
host.Run();
