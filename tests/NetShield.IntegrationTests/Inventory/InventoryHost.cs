using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Messaging;

using NetShield.Identity;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory;
using NetShield.Inventory.Clients;
using NetShield.Inventory.Collector;
using NetShield.Inventory.Credentials;
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Endpoints;
using NetShield.Inventory.Persistence;
using NetShield.Inventory.Reachability;
using NetShield.Inventory.Resolution;
using NetShield.Inventory.Topology;

using NetShield.Platform;
using NetShield.Platform.Auditing;
using NetShield.Platform.Messaging;
using NetShield.Platform.Persistence;
using NetShield.Platform.Problems;
using NetShield.Platform.Results;

using StackExchange.Redis;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// A host wired the way <c>NetShield.Web.Host</c> wires inventory, against a database of its own
/// on a real loopback port, with a signed-in session on the client.
/// </summary>
/// <remarks>
/// CONVENTIONS.md §7 admits no in-memory provider, and every guarantee this package makes needs
/// the real thing: a partial unique index, an <c>inet</c> column, a <c>text[]</c> containment
/// filter, a keyset comparison under the database's collation, and a transaction that carries a
/// device row and an outbox row together or neither.
/// </remarks>
internal sealed class InventoryHost(
    WebApplication application,
    SessionClient client,
    CollectorClient collector,
    string connectionString,
    RecordingLoggerProvider logs) : IAsyncDisposable
{
    /// <summary>The password every account this harness creates signs in with.</summary>
    internal const string Password = "Correct-Horse-Battery-9";

    /// <summary>The work factor the suite hashes at. The floor the options allow, for speed.</summary>
    private const string TestMemoryKib = "8192";

    /// <summary>The key id every profile in this suite is sealed under.</summary>
    internal const string ActiveKeyId = "test";

    /// <summary>
    /// A fixture key-encryption key: base64 of the bytes 0x00 to 0x1f, in order. Recognisably not
    /// a key anybody generated, and it opens nothing outside this suite's own throwaway database
    /// (CONVENTIONS.md §9).
    /// </summary>
    internal const string KeyEncryptionKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>A second fixture key, for the rotation tests. Bytes 0x20 to 0x3f.</summary>
    internal const string RotatedKeyEncryptionKey = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=";

    /// <summary>
    /// The shared secret this suite's collector presents. Recognisably a fixture, and long enough
    /// to clear the floor the options validator enforces (CONVENTIONS.md §9).
    /// </summary>
    internal const string CollectorSharedSecret = "integration-test-collector-secret-0000000000";

    /// <summary>What the collector in this suite calls itself.</summary>
    internal const string CollectorName = "collector-test";

    /// <summary>The client, presenting the shared secret and holding no session.</summary>
    public CollectorClient Collector => collector;

    /// <summary>The client, holding whatever cookies the API has set on it.</summary>
    public SessionClient Client => client;

    /// <summary>Where the host is listening, for a test that needs a client with no session.</summary>
    public Uri BaseAddress => new(application.Urls.First());

    /// <summary>
    /// The database this host was built against, so a second host can be started over the same
    /// rows with a different key ring — which is what a key rotation actually is.
    /// </summary>
    public string ConnectionString => connectionString;

    /// <summary>Starts the host and signs in as <paramref name="role"/>.</summary>
    /// <param name="postgres">The container the database is created on.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="role">The role the client signs in as.</param>
    /// <param name="database">
    /// An existing database to start against, rather than a fresh one. A rotation test needs two
    /// hosts over one set of rows.
    /// </param>
    /// <param name="keyRing">
    /// The key-encryption keys this host holds, defaulting to <see cref="KeyEncryptionKey"/>
    /// under <see cref="ActiveKeyId"/>.
    /// </param>
    /// <param name="activeKeyId">Which of them new material is sealed under.</param>
    /// <param name="leaseSeconds">
    /// How long a collector lease lasts. The default is the API's own; an expiry test asks for
    /// the shortest the options allow rather than waiting five minutes for one.
    /// </param>
    /// <param name="maxAttempts">How many leases a job gets before it is abandoned.</param>
    /// <param name="collectorSecret">
    /// The shared secret this host will accept, so a test can start one whose secret is not the
    /// one its client presents.
    /// </param>
    /// <param name="reachability">
    /// How the reachability schedule is configured, defaulting to intervals short enough that a
    /// test does not wait a minute for a device to fall due.
    /// </param>
    public static async Task<InventoryHost> StartAsync(
        PostgresFixture postgres,
        CancellationToken cancellationToken,
        UserRole role = UserRole.Administrator,
        string? database = null,
        IReadOnlyList<(string Id, string Key)>? keyRing = null,
        string? activeKeyId = null,
        int leaseSeconds = 300,
        int maxAttempts = 3,
        string collectorSecret = CollectorSharedSecret,
        ReachabilitySettings? reachability = null,
        DiscoverySettings? discovery = null,
        string? redisConnectionString = null,
        ClientSettings? clients = null,
        TopologySettings? topology = null)
    {
        ReachabilitySettings probes = reachability ?? new ReachabilitySettings();
        DiscoverySettings sweeps = discovery ?? new DiscoverySettings();
        ClientSettings tracking = clients ?? new ClientSettings();
        TopologySettings graph = topology ?? new TopologySettings();

        string connectionString = database ?? await postgres.CreateDatabaseAsync(cancellationToken);

        IReadOnlyList<(string Id, string Key)> keys = keyRing ?? [(ActiveKeyId, KeyEncryptionKey)];

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(InventoryHost).Assembly.GetName().Name
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Identity:PasswordHashing:MemoryKib"] = TestMemoryKib,
            ["Identity:PasswordHashing:Iterations"] = "1",
            ["Security:CredentialEncryption:ActiveKeyId"] = activeKeyId ?? keys[0].Id,
            ["Collector:SharedSecret"] = collectorSecret,
            ["Collector:Jobs:LeaseSeconds"] = leaseSeconds.ToString(CultureInfo.InvariantCulture),
            ["Collector:Jobs:MaxAttempts"] = maxAttempts.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Reachability:PollIntervalSeconds"] =
                probes.PollIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Reachability:ScanIntervalSeconds"] =
                probes.ScanIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Reachability:MaxJobsPerScan"] =
                probes.MaxJobsPerScan.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Reachability:FailureThreshold"] =
                probes.FailureThreshold.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Reachability:SuccessThreshold"] =
                probes.SuccessThreshold.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Discovery:MaxAddressesPerJob"] =
                sweeps.MaxAddressesPerJob.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Discovery:MaxAddressesPerRun"] =
                sweeps.MaxAddressesPerRun.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Discovery:MaxJobsPerRun"] =
                sweeps.MaxJobsPerRun.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Discovery:MaxRunsPerScan"] =
                sweeps.MaxRunsPerScan.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Clients:WalkIntervalSeconds"] =
                tracking.WalkIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Clients:ScanIntervalSeconds"] =
                tracking.ScanIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Clients:MaxJobsPerScan"] =
                tracking.MaxJobsPerScan.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Clients:ResolutionCacheSeconds"] =
                tracking.ResolutionCacheSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Topology:Enabled"] = graph.Enabled ? "true" : "false",
            ["Inventory:Topology:NeighborWalkIntervalSeconds"] =
                graph.NeighborWalkIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Topology:RouteWalkIntervalSeconds"] =
                graph.RouteWalkIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Topology:ScanIntervalSeconds"] =
                graph.ScanIntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["Inventory:Topology:MaxJobsPerScan"] =
                graph.MaxJobsPerScan.ToString(CultureInfo.InvariantCulture)
        };

        for (int index = 0; index < sweeps.CredentialKindOrder.Count; index++)
        {
            settings[$"Inventory:Discovery:CredentialKindOrder:{index}"] =
                sweeps.CredentialKindOrder[index].ToString();
        }

        foreach ((string id, string key) in keys)
        {
            settings[$"Security:CredentialEncryption:Keys:{id}"] = key;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        // Everything is recorded, at every level, so that "no credential in any log line" is a
        // claim a test can check rather than one the default filters made true by accident.
        RecordingLoggerProvider logs = new();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddSingleton<ILoggerProvider>(logs);

        // `EnableRetryOnFailure` mirrors what `AddNpgsqlDbContext` configures in the composition
        // root. It is not here for the retries — a Testcontainers database on loopback has no
        // transient faults worth retrying — but because a retrying execution strategy *refuses*
        // an explicit transaction it did not open, and a handler that opens one therefore fails
        // in the running system while passing every test. That is exactly how the collector's
        // lease endpoint came to answer 500 to every call in `aspire run` with a green suite
        // behind it. The test host has to be wrong in the same ways production is, or it is
        // testing a system nobody runs.
        builder.Services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
                .UseNetShieldConventions());
        builder.Services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
                .UseIdentityConventions());
        builder.Services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
                .UseInventoryConventions());

        // Registered before the platform, because the platform's cache resolves from whether a
        // Redis connection is there: a host given one gets the real store and a host given none
        // gets the null store. Both are legitimate configurations (ARCHITECTURE.md §3), and the
        // resolution suite runs against each so the two cannot come to disagree about an answer.
        if (redisConnectionString is not null)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(
                await ConnectionMultiplexer.ConnectAsync(redisConnectionString));
        }

        builder.AddNetShieldPlatform();
        builder.Services.AddNetShieldProblemDetails();
        builder.AddNetShieldAuthorization();
        builder.AddNetShieldAudit();
        builder.AddNetShieldIdentity();
        builder.AddNetShieldInventory();

        // What RewrapMode registers in NetShield.Web.Host. The rewrapper is not part of the API's
        // own registration — the API never rotates a key — so a test that exercises it has to
        // register it the same way the command does.
        builder.Services.AddScoped<CredentialKeyRewrapper>();

        WebApplication application = builder.Build();

        // Applied before the host starts, for the reason the identity harness applies its own:
        // NetShield.Web.Host does not migrate on startup, so the database belongs to whoever
        // created it. Platform first — it owns outbox_messages, which the inventory context maps.
        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
                .Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(cancellationToken);
        }

        application.UseNetShieldProblemDetails();
        application.UseAuthentication();
        application.UseNetShieldAudit();
        application.UseAuthorization();
        application.MapIdentityEndpoints();
        application.MapInventoryEndpoints();

        await application.StartAsync(cancellationToken);

        Uri baseAddress = new(application.Urls.First());

        SessionClient client = new(new HttpClient { BaseAddress = baseAddress });
        CollectorClient collector = new(baseAddress, CollectorSharedSecret);

        InventoryHost host = new(application, client, collector, connectionString, logs);

        await host.SignInAsync(role, cancellationToken);

        return host;
    }

    /// <summary>Creates an account in <paramref name="role"/> and signs the client in as it.</summary>
    public async Task SignInAsync(UserRole role, CancellationToken cancellationToken)
    {
        string username = $"user-{Guid.CreateVersion7():N}";

        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            IdentityDbContext identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            DateTimeOffset now = DateTimeOffset.UtcNow;

            identity.Users.Add(new User
            {
                Id = Guid.CreateVersion7(now),
                Username = username,
                NormalizedUsername = UserName.Normalize(username),
                DisplayName = username,
                PasswordHash = await hasher.HashAsync(Password, cancellationToken),
                Role = role,
                IsActive = true,
                PasswordChangedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });

            await identity.SaveChangesAsync(cancellationToken);
        }

        ApiResponse response = await client.PostAsync(
            "/api/v1/auth/login",
            new LoginRequest(username, Password),
            cancellationToken);

        if (response.Status != 200)
        {
            throw new InvalidOperationException($"Could not sign in as {role}: {response.Status} {response.Body}");
        }
    }

    /// <summary>Every outbox row written so far, oldest first, as its registered event name.</summary>
    public async Task<IReadOnlyList<string>> OutboxEventNamesAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .OutboxMessages.AsNoTracking()
            .OrderBy(message => message.Id)
            .Select(message => message.EventType)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The payload of the most recent outbox row, so a test can read what it carried.</summary>
    public async Task<string?> LastOutboxPayloadAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .OutboxMessages.AsNoTracking()
            .OrderByDescending(message => message.Id)
            .Select(message => message.Payload)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The audit rows for a target, so a test can assert one was written per mutation.</summary>
    public async Task<IReadOnlyList<AuditRow>> AuditRowsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .Set<AuditEntry>().AsNoTracking()
            .OrderBy(entry => entry.Id)
            .Select(entry => new AuditRow(entry.Action, entry.TargetType, entry.TargetId, entry.Outcome))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The <c>after</c> snapshots of every audit row with this action, as they were stored — that
    /// is, after redaction.
    /// </summary>
    public async Task<IReadOnlyList<string>> AuditSnapshotsAsync(
        string action,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .Set<AuditEntry>().AsNoTracking()
            .Where(entry => entry.Action == action && entry.After != null)
            .Select(entry => entry.After!)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The bytes actually written for a profile, straight off the row. What a test needs to show
    /// that the column holds no plaintext and that a rotation moved the key.
    /// </summary>
    public async Task<StoredCiphertext> CiphertextAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        return await context.CredentialProfiles.AsNoTracking()
            .Where(profile => profile.Id == profileId)
            .Select(profile => new StoredCiphertext(
                profile.KeyId,
                profile.WrappedDataKey,
                profile.MaterialCiphertext))
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Every log line this host has written, message and structured values alike, as they reached
    /// the sink — which is to say after the platform's redaction.
    /// </summary>
    public IReadOnlyList<string> RecordedLogs() =>
        [.. logs.Records.SelectMany(record => (string[])[record.Message, .. record.Values])];

    /// <summary>Runs something inside a request scope — the decrypt path, or the rewrapper.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await work(scope.ServiceProvider);
    }

    /// <summary>Reads a device's <c>deleted_at</c> directly, which no endpoint exposes.</summary>
    public async Task<DateTimeOffset?> DeletedAtAsync(Guid id, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        return await context.Devices.AsNoTracking()
            .Where(device => device.Id == id)
            .Select(device => device.DeletedAt)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Queues a job through the module's own enqueue port, which is how WP-1.4 and WP-1.6 will.
    /// </summary>
    /// <remarks>
    /// There is no route for this and there is not meant to be: WP-1.3 builds the lease model,
    /// and the packages that decide what to collect own the scheduling that fills the queue.
    /// </remarks>
    public Task<Guid> EnqueueAsync(
        NewCollectorJob job,
        CancellationToken cancellationToken) =>
        InScopeAsync(async services =>
        {
            Result<Guid> queued = await services.GetRequiredService<ICollectorJobQueue>()
                .EnqueueAsync(job, cancellationToken);

            if (!queued.IsSuccess)
            {
                throw new InvalidOperationException($"Could not queue the job: {queued.Error.Message}");
            }

            return queued.Value;
        });

    /// <summary>One collector job row, reduced to what these tests assert on.</summary>
    public async Task<CollectorJobRow> JobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .CollectorJobs.AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new CollectorJobRow(
                job.Status,
                job.Outcome,
                job.Attempts,
                job.LeaseToken,
                job.LeasedBy,
                job.Detail,
                job.Result,
                job.CredentialProfileId))
            .SingleAsync(cancellationToken);
    }

    /// <summary>Every collector job, oldest first, with the parameters it carries.</summary>
    public async Task<IReadOnlyList<CollectorJobParametersRow>> CollectorJobsAsync(
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        // The coalesce is in memory rather than in the query: `parameters` is a json column, and
        // COALESCE(parameters, '') asks PostgreSQL to read an empty string as JSON.
        var rows = await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .CollectorJobs.AsNoTracking()
            .OrderBy(job => job.Id)
            .Select(job => new
            {
                job.Id,
                job.DeviceId,
                job.Kind,
                job.Status,
                job.Parameters
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new CollectorJobParametersRow(
                row.Id,
                row.DeviceId,
                row.Kind,
                row.Status,
                row.Parameters ?? string.Empty))
        ];
    }

    /// <summary>Every live device's id, oldest first.</summary>
    public async Task<IReadOnlyList<Guid>> DeviceIdsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Devices.AsNoTracking()
            .Where(device => device.DeletedAt == null)
            .OrderBy(device => device.Id)
            .Select(device => device.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Moves a job's lease into the past, which is what waiting for one to expire does.</summary>
    public async Task ExpireLeaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        CollectorJob job = await context.CollectorJobs.SingleAsync(candidate => candidate.Id == jobId, cancellationToken);

        job.LeasedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>What a collector has reported about itself, or nothing if none has.</summary>
    public async Task<CollectorNodeRow?> CollectorNodeAsync(string name, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        string normalized = name.ToUpperInvariant();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .CollectorNodes.AsNoTracking()
            .Where(node => node.NormalizedName == normalized)
            .Select(node => new CollectorNodeRow(node.Name, node.Version, node.Capacity, node.Running))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Runs one pass of the reachability schedule, which is what the background loop does on a
    /// timer in the API. The loop itself is not registered here — a test drives passes.
    /// </summary>
    /// <returns>How many probes were queued.</returns>
    public Task<int> ScheduleReachabilityAsync(CancellationToken cancellationToken) =>
        InScopeAsync(services => services.GetRequiredService<ReachabilitySchedulePass>()
            .ScheduleDueAsync(cancellationToken));

    /// <summary>
    /// Runs one pass of the discovery schedule, which is what the background loop does on a
    /// timer in the API. The loop itself is not registered here, for the same reason.
    /// </summary>
    /// <returns>How many runs were started.</returns>
    public Task<int> ScheduleDiscoveryAsync(CancellationToken cancellationToken) =>
        InScopeAsync(services => services.GetRequiredService<DiscoverySchedulePass>()
            .ScheduleDueAsync(cancellationToken));

    /// <summary>
    /// Runs one pass of the client schedule, which is what the background loop does on a timer
    /// in the API. The loop itself is not registered here, for the same reason.
    /// </summary>
    /// <returns>How many client walks were queued.</returns>
    public Task<int> ScheduleClientsAsync(CancellationToken cancellationToken) =>
        InScopeAsync(services => services.GetRequiredService<ClientSchedulePass>()
            .ScheduleDueAsync(cancellationToken));

    /// <summary>
    /// Runs one pass of the topology schedule, which is what the background loop does on a timer
    /// in the API. The loop itself is not registered here, for the same reason.
    /// </summary>
    /// <returns>How many topology walks were queued.</returns>
    public Task<int> ScheduleTopologyAsync(CancellationToken cancellationToken) =>
        InScopeAsync(services => services.GetRequiredService<TopologySchedulePass>()
            .ScheduleDueAsync(cancellationToken));

    /// <summary>
    /// Resolves an address at an instant through the module's own port, which is how
    /// <c>NetShield.Ingest</c> will once Phase 4 wires the enrichment path.
    /// </summary>
    /// <remarks>
    /// Reached out of the container by hand because <c>IAssetResolver</c> is internal to the
    /// module and has no HTTP surface of its own — the same shape <c>ICredentialResolver</c> is
    /// exercised in. The route at <c>/api/v1/clients/resolve</c> is tested through the client.
    /// </remarks>
    public Task<AssetResolution> ResolveAsync(
        string address,
        DateTimeOffset at,
        CancellationToken cancellationToken) =>
        InScopeAsync(services => services.GetRequiredService<IAssetResolver>()
            .ResolveAtAsync(System.Net.IPAddress.Parse(address), at, cancellationToken));

    /// <summary>
    /// Times many warm resolutions of one address inside a single scope, and returns them in
    /// microseconds, sorted.
    /// </summary>
    /// <remarks>
    /// One scope for the whole run, deliberately. A scope per call would also build an
    /// <c>InventoryDbContext</c> each time, and the measurement would then be mostly about the
    /// dependency-injection container — which is not what "resolution is under 1 ms warm" is a
    /// claim about, and is the part most sensitive to whatever else the machine is doing. It is
    /// also the shape the caller will have: ARCHITECTURE.md §6 puts resolution inside the ingest
    /// pipeline, which enriches many events per scope rather than one.
    /// </remarks>
    /// <param name="address">The address to resolve.</param>
    /// <param name="at">The instant to resolve it at.</param>
    /// <param name="iterations">How many timed resolutions to make.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="warmup">
    /// How many untimed resolutions to make first. Zero times a genuinely cold read — the first
    /// call for an address is the one that queries PostgreSQL and writes the entry back.
    /// </param>
    public async Task<double[]> TimeResolutionsAsync(
        string address,
        DateTimeOffset at,
        int iterations,
        CancellationToken cancellationToken,
        int warmup = 10)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        IAssetResolver resolver = scope.ServiceProvider.GetRequiredService<IAssetResolver>();
        System.Net.IPAddress parsed = System.Net.IPAddress.Parse(address);

        // Warm the cache entry, and let the Redis connection settle, before anything is timed.
        for (int index = 0; index < warmup; index++)
        {
            await resolver.ResolveAtAsync(parsed, at, cancellationToken);
        }

        double[] microseconds = new double[iterations];

        for (int index = 0; index < iterations; index++)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            await resolver.ResolveAtAsync(parsed, at, cancellationToken);

            microseconds[index] =
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }

        Array.Sort(microseconds);

        return microseconds;
    }

    /// <summary>Every client, oldest first.</summary>
    public async Task<IReadOnlyList<ClientRow>> ClientsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Clients.AsNoTracking()
            .OrderBy(client => client.Id)
            .Select(client => new ClientRow(
                client.Id,
                client.MacAddress,
                client.Oui,
                client.LocallyAdministered,
                client.FirstSeenAt,
                client.LastSeenAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Every address interval, newest first, with the MAC that held it.</summary>
    public async Task<IReadOnlyList<ClientIpBindingRow>> IpBindingsAsync(
        CancellationToken cancellationToken,
        string? address = null)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var rows = await (
            from binding in context.ClientIpBindings.AsNoTracking()
            join client in context.Clients.AsNoTracking() on binding.ClientId equals client.Id
            orderby binding.ObservedFrom descending, binding.Id descending
            select new
            {
                binding.ClientId,
                client.MacAddress,
                binding.IpAddress,
                binding.ObservedFrom,
                binding.ObservedTo,
                binding.LastSeenAt
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .Select(row => new ClientIpBindingRow(
                    row.ClientId,
                    row.MacAddress,
                    row.IpAddress.ToString(),
                    row.ObservedFrom,
                    row.ObservedTo,
                    row.LastSeenAt))
                .Where(row => address is null || row.IpAddress == address)
        ];
    }

    /// <summary>Every port interval, newest first, with the MAC it reported.</summary>
    public async Task<IReadOnlyList<ClientPortBindingRow>> PortBindingsAsync(
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        return await (
            from binding in context.ClientPortBindings.AsNoTracking()
            join client in context.Clients.AsNoTracking() on binding.ClientId equals client.Id
            orderby binding.ObservedFrom descending, binding.Id descending
            select new ClientPortBindingRow(
                binding.ClientId,
                client.MacAddress,
                binding.DeviceId,
                binding.IfIndex,
                binding.VlanId,
                binding.MacCountOnPort,
                binding.ObservedFrom,
                binding.ObservedTo))
            .ToListAsync(cancellationToken);
    }

    /// <summary>What reading a device's client tables established, or nothing if none has been read.</summary>
    public async Task<ClientScanRow?> ClientScanAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceClientScans.AsNoTracking()
            .Where(row => row.DeviceId == deviceId)
            .Select(row => new ClientScanRow(
                row.NextWalkAt,
                row.LastWalkAt,
                row.LastAppliedJobId,
                row.NeighborsSupported,
                row.ForwardingSupported,
                row.LastNeighborCount,
                row.LastForwardingCount,
                row.LastError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Moves a device's next client walk into the past, which waiting for one does.</summary>
    public async Task MakeClientScanDueAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        DeviceClientScan scan = await context.DeviceClientScans
            .SingleAsync(row => row.DeviceId == deviceId, cancellationToken);

        scan.NextWalkAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds an address interval directly, for a test about resolution rather than about walking.
    /// </summary>
    /// <remarks>
    /// It writes what a walk would have written, including the invariants: the caller says which
    /// interval it wants and the partial unique index refuses a second open one for an address,
    /// so a fixture that produced an impossible history fails here rather than in an assertion.
    /// </remarks>
    public async Task<Guid> SeedBindingAsync(
        string mac,
        string address,
        DateTimeOffset observedFrom,
        DateTimeOffset? observedTo,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        string normalized = mac.ToUpperInvariant();

        Client? client = await context.Clients
            .SingleOrDefaultAsync(row => row.MacAddress == normalized, cancellationToken);

        if (client is null)
        {
            client = new Client
            {
                Id = Guid.CreateVersion7(observedFrom),
                MacAddress = normalized,
                Oui = normalized[..8],
                LocallyAdministered = false,
                FirstSeenAt = observedFrom,
                LastSeenAt = observedTo ?? observedFrom,
                CreatedAt = observedFrom,
                UpdatedAt = observedFrom
            };

            context.Clients.Add(client);
        }

        context.ClientIpBindings.Add(new ClientIpBinding
        {
            Id = Guid.CreateVersion7(observedFrom),
            ClientId = client.Id,
            IpAddress = System.Net.IPAddress.Parse(address),
            Source = ClientObservationSource.ArpTable,
            ObservedFrom = observedFrom,
            ObservedTo = observedTo,
            LastSeenAt = observedTo ?? observedFrom,
            CreatedAt = observedFrom,
            UpdatedAt = observedFrom
        });

        await context.SaveChangesAsync(cancellationToken);

        return client.Id;
    }

    /// <summary>
    /// Seeds an estate of clients, each holding one address, for a test about scale.
    /// </summary>
    /// <remarks>
    /// SPEC.md §1 targets 5,000 tracked clients, which is what a warm resolution has to stay
    /// under a millisecond at. Written in one batch rather than through the walk path: what is
    /// being measured is the read, and building the rows through the collector round trip would
    /// spend a minute proving something the round-trip suite already proves.
    /// </remarks>
    public async Task SeedClientsAsync(int count, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        DateTimeOffset now = DateTimeOffset.UtcNow.AddHours(-1);

        for (int index = 0; index < count; index++)
        {
            // 10.64.0.1 upwards, which stays inside one /16 for the whole target scale.
            string address = $"10.64.{(index / 254) & 0xFF}.{(index % 254) + 1}";
            string mac = $"AA:BB:CC:{(index >> 16) & 0xFF:X2}:{(index >> 8) & 0xFF:X2}:{index & 0xFF:X2}";

            Guid clientId = Guid.CreateVersion7(now);

            context.Clients.Add(new Client
            {
                Id = clientId,
                MacAddress = mac,
                Oui = mac[..8],
                LocallyAdministered = true,
                FirstSeenAt = now,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });

            context.ClientIpBindings.Add(new ClientIpBinding
            {
                Id = Guid.CreateVersion7(now),
                ClientId = clientId,
                IpAddress = System.Net.IPAddress.Parse(address),
                Source = ClientObservationSource.ArpTable,
                ObservedFrom = now,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Delivers every pending outbox row, which is what makes a subscriber run.
    /// </summary>
    /// <remarks>
    /// The dispatcher is a hosted service this harness does not register, for the reason it does
    /// not register the reachability loop: a test that has to wait for a timer is a test that is
    /// sometimes flaky. This is the same processor the dispatcher drives, driven a pass at a time.
    /// </remarks>
    public async Task DispatchOutboxAsync(CancellationToken cancellationToken)
    {
        while (await InScopeAsync(services =>
            services.GetRequiredService<OutboxProcessor>().DispatchPendingAsync(cancellationToken)) > 0)
        {
            // Keep going while rows are still being delivered: one handler can enlist another row.
        }
    }

    /// <summary>
    /// Marks every delivered outbox row pending again and dispatches, which is what an
    /// at-least-once bus does when a delivery is not recorded before the process dies.
    /// </summary>
    public async Task RedeliverOutboxAsync(CancellationToken cancellationToken)
    {
        await using (AsyncServiceScope scope = application.Services.CreateAsyncScope())
        {
            PlatformDbContext platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

            await platform.OutboxMessages
                .Where(message => message.ProcessedAt != null)
                .ExecuteUpdateAsync(
                    message => message.SetProperty(row => row.ProcessedAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        await DispatchOutboxAsync(cancellationToken);
    }

    /// <summary>Every outbox row of one event type, deserialised, oldest first.</summary>
    public async Task<IReadOnlyList<TEvent>> OutboxPayloadsAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        string eventType = typeof(TEvent).FullName!;

        List<string> payloads = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .OutboxMessages.AsNoTracking()
            .Where(message => message.EventType == eventType)
            .OrderBy(message => message.Id)
            .Select(message => message.Payload)
            .ToListAsync(cancellationToken);

        return [.. payloads.Select(payload =>
            (TEvent)OutboxPayload.Deserialize(payload, typeof(TEvent))!)];
    }

    /// <summary>What is known about a device's reachability, or nothing if it has never been due.</summary>
    public async Task<ReachabilityRow?> ReachabilityAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceReachabilities.AsNoTracking()
            .Where(row => row.DeviceId == deviceId)
            .Select(row => new ReachabilityRow(
                row.PendingState,
                row.PendingObservations,
                row.NextProbeAt,
                row.LastProbeAt,
                row.LastChangedAt,
                row.LastRttMilliseconds,
                row.LastLossPercent,
                row.LastAppliedJobId,
                row.LastError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>A device's published state, straight off the row.</summary>
    public async Task<DeviceState> DeviceStateAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Devices.AsNoTracking()
            .Where(device => device.Id == deviceId)
            .Select(device => device.State)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Every job queued for a device, oldest first.</summary>
    public async Task<IReadOnlyList<Guid>> JobIdsForAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .CollectorJobs.AsNoTracking()
            .Where(job => job.DeviceId == deviceId)
            .OrderBy(job => job.Id)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The parameter document a queued job carries.</summary>
    public async Task<string?> JobParametersAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .CollectorJobs.AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.Parameters)
            .SingleAsync(cancellationToken);
    }

    /// <summary>The sweep jobs one discovery run fanned out into, oldest first.</summary>
    public async Task<IReadOnlyList<DiscoveryRunJobRow>> RunJobsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DiscoveryRunJobs.AsNoTracking()
            .Where(job => job.RunId == runId)
            .OrderBy(job => job.Sequence)
            .Select(job => new DiscoveryRunJobRow(
                job.CollectorJobId,
                job.FirstAddress,
                job.LastAddress,
                job.AddressCount,
                job.AppliedAt,
                job.Succeeded))
            .ToListAsync(cancellationToken);
    }

    /// <summary>When a seed is next due, which no endpoint exposes for a disabled seed.</summary>
    public async Task<DateTimeOffset?> SeedNextRunAtAsync(Guid seedId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DiscoverySeeds.AsNoTracking()
            .Where(seed => seed.Id == seedId)
            .Select(seed => seed.NextRunAt)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Moves a seed's next run into the past, which is what waiting for one to fall due does.</summary>
    public async Task MakeSeedDueAsync(Guid seedId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        DiscoverySeed seed = await context.DiscoverySeeds
            .SingleAsync(candidate => candidate.Id == seedId, cancellationToken);

        seed.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>What the last SNMP walk established, or nothing if none has ever run.</summary>
    public async Task<FingerprintRow?> FingerprintAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceFingerprints.AsNoTracking()
            .Where(row => row.DeviceId == deviceId)
            .Select(row => new FingerprintRow(
                row.Vendor,
                row.ReducedCapability,
                row.SysObjectId,
                row.SysDescr,
                row.SysName,
                row.UptimeSeconds,
                row.Model,
                row.OsVersion,
                row.SerialNumber,
                row.InterfaceCount,
                row.InterfacesTruncated,
                row.OverriddenFields,
                row.LastWalkAt,
                row.LastAppliedJobId,
                row.LastError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Every live edge, oldest first.</summary>
    public async Task<IReadOnlyList<AdjacencyRow>> AdjacenciesAsync(
        CancellationToken cancellationToken,
        bool includeWithdrawn = false)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceAdjacencies.AsNoTracking()
            .Where(edge => includeWithdrawn || edge.WithdrawnAt == null)
            .OrderBy(edge => edge.Id)
            .Select(edge => new AdjacencyRow(
                edge.Id,
                edge.ADeviceId,
                edge.AIfIndex,
                edge.AInterfaceName,
                edge.BDeviceId,
                edge.BIfIndex,
                edge.BChassisId,
                edge.BChassisIdKind,
                edge.BPortId,
                edge.BSystemName,
                edge.SourcesA,
                edge.SourcesB,
                edge.Confidence,
                edge.ObservedFromA,
                edge.ObservedFromB,
                edge.FirstDiscoveredAt,
                edge.LastSeenAt,
                edge.WithdrawnAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Every neighbour observation, oldest first.</summary>
    public async Task<IReadOnlyList<NeighborRow>> NeighborsAsync(
        CancellationToken cancellationToken,
        bool includeWithdrawn = false)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceNeighbors.AsNoTracking()
            .Where(row => includeWithdrawn || row.WithdrawnAt == null)
            .OrderBy(row => row.Id)
            .Select(row => new NeighborRow(
                row.DeviceId,
                row.Source,
                row.LocalIfIndex,
                row.RemoteChassisId,
                row.RemotePortId,
                row.RemoteDeviceId,
                row.RemoteIfIndex,
                row.AdjacencyId,
                row.EvidenceCount,
                row.FirstDiscoveredAt,
                row.LastSeenAt,
                row.WithdrawnAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>One device's topology scan row, or nothing when nothing has walked it.</summary>
    public async Task<TopologyScanRow?> TopologyScanAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceTopologyScans.AsNoTracking()
            .Where(scan => scan.DeviceId == deviceId)
            .Select(scan => new TopologyScanRow(
                scan.NextNeighborWalkAt,
                scan.LastNeighborWalkAt,
                scan.LldpSupported,
                scan.CdpSupported,
                scan.LastLldpCount,
                scan.LastCdpCount,
                scan.LastNeighborError,
                scan.NextRouteWalkAt,
                scan.LastRouteWalkAt,
                scan.RoutingSupported,
                scan.RouteTable,
                scan.LastRouteCount,
                scan.LastRouteError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Records interfaces for a device without running a fingerprint walk.
    /// </summary>
    /// <remarks>
    /// The topology reconciler resolves a neighbour's advertised port against the far device's
    /// interface inventory, which WP-1.5's walk populates. Seeding it directly keeps a test about
    /// adjacency from having to drive a second walk of a second device to establish something the
    /// fingerprint suite already proves.
    /// </remarks>
    public async Task SeedInterfacesAsync(
        Guid deviceId,
        IReadOnlyList<(int IfIndex, string Name, string Description, string? PhysicalAddress)> ports,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach ((int ifIndex, string name, string description, string? physical) in ports)
        {
            context.DeviceInterfaces.Add(new DeviceInterface
            {
                Id = Guid.CreateVersion7(now),
                DeviceId = deviceId,
                IfIndex = ifIndex,
                Name = name,
                Description = description,
                PhysicalAddress = physical,
                FirstSeenAt = now,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A device's interface inventory, in ifIndex order.</summary>
    public async Task<IReadOnlyList<InterfaceRow>> InterfacesAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .DeviceInterfaces.AsNoTracking()
            .Where(row => row.DeviceId == deviceId)
            .OrderBy(row => row.IfIndex)
            .Select(row => new InterfaceRow(
                row.IfIndex,
                row.Name,
                row.Description,
                row.Alias,
                row.InterfaceType,
                row.Mtu,
                row.SpeedBitsPerSecond,
                row.PhysicalAddress,
                row.AdminStatus,
                row.OperStatus,
                row.FirstSeenAt,
                row.LastSeenAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The four identity facts as they stand on the device row itself.</summary>
    public async Task<DeviceFacts> DeviceFactsAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Devices.AsNoTracking()
            .Where(device => device.Id == deviceId)
            .Select(device => new DeviceFacts(
                device.Vendor,
                device.Model,
                device.OsVersion,
                device.SerialNumber,
                device.UpdatedAt))
            .SingleAsync(cancellationToken);
    }

    /// <summary>Brings a device's next probe forward, which is what waiting an interval does.</summary>
    public async Task MakeDueAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        InventoryDbContext context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        DeviceReachability row = await context.DeviceReachabilities
            .SingleAsync(candidate => candidate.DeviceId == deviceId, cancellationToken);

        row.NextProbeAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        collector.Dispose();

        await application.StopAsync();
        await application.DisposeAsync();
    }
}

/// <summary>One audit row, reduced to what these tests assert on.</summary>
internal sealed record AuditRow(string Action, string? TargetType, string? TargetId, AuditOutcome Outcome);

/// <summary>One collector job, reduced to what these tests assert on.</summary>
/// <summary>One queued job, reduced to what a schedule test asserts on.</summary>
internal sealed record CollectorJobParametersRow(
    Guid Id,
    Guid? DeviceId,
    CollectorJobKind Kind,
    CollectorJobStatus Status,
    string Parameters);

internal sealed record CollectorJobRow(
    CollectorJobStatus Status,
    CollectorJobOutcome? Outcome,
    int Attempts,
    string? LeaseToken,
    string? LeasedBy,
    string? Detail,
    string? Result,
    Guid? CredentialProfileId);

/// <summary>What a test wants the reachability schedule configured as.</summary>
/// <remarks>
/// Defaults that make a test fast rather than realistic: the estate is probed on a short
/// interval, and the thresholds are the smallest numbers that still distinguish "once" from
/// "enough times".
/// </remarks>
/// <summary>
/// How the discovery schedule is configured for a test, so that a span is a handful of addresses
/// rather than a /24 and a ceiling can be crossed on purpose.
/// </summary>
internal sealed record DiscoverySettings(
    int MaxAddressesPerJob = 64,
    long MaxAddressesPerRun = 1024,
    int MaxJobsPerRun = 512,
    int MaxRunsPerScan = 2)
{
    /// <summary>The credential kind order this host resolves an SNMP walk's credential by.</summary>
    public IReadOnlyList<CredentialKind> CredentialKindOrder { get; init; } =
        [CredentialKind.SnmpV3, CredentialKind.SnmpV2c];
}

internal sealed record ReachabilitySettings(
    int PollIntervalSeconds = 10,
    int ScanIntervalSeconds = 1,
    int MaxJobsPerScan = 500,
    int FailureThreshold = 3,
    int SuccessThreshold = 2);

/// <summary>
/// How this host tracks clients, at intervals short enough that a test does not wait a quarter of
/// an hour for a device to fall due.
/// </summary>
internal sealed record ClientSettings(
    int WalkIntervalSeconds = 60,
    int ScanIntervalSeconds = 1,
    int MaxJobsPerScan = 100,
    int ResolutionCacheSeconds = 300);

/// <summary>
/// How the topology schedule is configured for a test.
/// </summary>
/// <remarks>
/// Defaults that make a test fast rather than realistic, the way the other three settings records
/// do. The route interval stays above the neighbour interval, because which of the two a pass
/// picks when both are due is a rule worth exercising as it actually runs.
/// </remarks>
internal sealed record TopologySettings(
    bool Enabled = true,
    int NeighborWalkIntervalSeconds = 60,
    int RouteWalkIntervalSeconds = 120,
    int ScanIntervalSeconds = 1,
    int MaxJobsPerScan = 100);

/// <summary>One client row, reduced to what these tests assert on.</summary>
internal sealed record ClientRow(
    Guid Id,
    string MacAddress,
    string Oui,
    bool LocallyAdministered,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>One address interval, reduced to what these tests assert on.</summary>
internal sealed record ClientIpBindingRow(
    Guid ClientId,
    string MacAddress,
    string IpAddress,
    DateTimeOffset ObservedFrom,
    DateTimeOffset? ObservedTo,
    DateTimeOffset LastSeenAt);

/// <summary>One port interval, reduced to what these tests assert on.</summary>
internal sealed record ClientPortBindingRow(
    Guid ClientId,
    string MacAddress,
    Guid DeviceId,
    int IfIndex,
    int? VlanId,
    int? MacCountOnPort,
    DateTimeOffset ObservedFrom,
    DateTimeOffset? ObservedTo);

/// <summary>One edge, reduced to what these tests assert on.</summary>
internal sealed record AdjacencyRow(
    Guid Id,
    Guid ADeviceId,
    int AIfIndex,
    string? AInterfaceName,
    Guid? BDeviceId,
    int? BIfIndex,
    string BChassisId,
    NeighborIdKind BChassisIdKind,
    string? BPortId,
    string? BSystemName,
    IReadOnlyList<string> SourcesA,
    IReadOnlyList<string> SourcesB,
    AdjacencyConfidence Confidence,
    bool ObservedFromA,
    bool ObservedFromB,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? WithdrawnAt)
{
    /// <summary>Every protocol supporting the edge, from either end.</summary>
    public IReadOnlyList<string> Sources => [.. SourcesA.Concat(SourcesB).Distinct().Order()];
}

/// <summary>One neighbour observation, reduced to what these tests assert on.</summary>
internal sealed record NeighborRow(
    Guid DeviceId,
    NeighborSource Source,
    int LocalIfIndex,
    string RemoteChassisId,
    string? RemotePortId,
    Guid? RemoteDeviceId,
    int? RemoteIfIndex,
    Guid? AdjacencyId,
    int? EvidenceCount,
    DateTimeOffset FirstDiscoveredAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? WithdrawnAt);

/// <summary>One topology-scan row, reduced to what these tests assert on.</summary>
internal sealed record TopologyScanRow(
    DateTimeOffset NextNeighborWalkAt,
    DateTimeOffset? LastNeighborWalkAt,
    bool? LldpSupported,
    bool? CdpSupported,
    int? LastLldpCount,
    int? LastCdpCount,
    string? LastNeighborError,
    DateTimeOffset NextRouteWalkAt,
    DateTimeOffset? LastRouteWalkAt,
    bool? RoutingSupported,
    string? RouteTable,
    int? LastRouteCount,
    string? LastRouteError);

/// <summary>One client-scan row, reduced to what these tests assert on.</summary>
internal sealed record ClientScanRow(
    DateTimeOffset NextWalkAt,
    DateTimeOffset? LastWalkAt,
    Guid? LastAppliedJobId,
    bool? NeighborsSupported,
    bool? ForwardingSupported,
    int? LastNeighborCount,
    int? LastForwardingCount,
    string? LastError);

/// <summary>One reachability row, reduced to what these tests assert on.</summary>
/// <summary>One sweep job of a run, reduced to what these tests assert on.</summary>
internal sealed record DiscoveryRunJobRow(
    Guid CollectorJobId,
    string FirstAddress,
    string LastAddress,
    int AddressCount,
    DateTimeOffset? AppliedAt,
    bool? Succeeded);

internal sealed record ReachabilityRow(
    DeviceState PendingState,
    int PendingObservations,
    DateTimeOffset NextProbeAt,
    DateTimeOffset? LastProbeAt,
    DateTimeOffset? LastChangedAt,
    double? LastRttMilliseconds,
    double? LastLossPercent,
    Guid? LastAppliedJobId,
    string? LastError);

/// <summary>One collector's self-reported state.</summary>
internal sealed record CollectorNodeRow(string Name, string? Version, int Capacity, int Running);

/// <summary>The three columns a sealed credential occupies.</summary>
/// <param name="KeyId">Which key-encryption key the wrapped data key is under.</param>
/// <param name="WrappedDataKey">The data key, sealed.</param>
/// <param name="MaterialCiphertext">The material, sealed.</param>
internal sealed record StoredCiphertext(string KeyId, byte[] WrappedDataKey, byte[] MaterialCiphertext);

/// <summary>What the last SNMP walk recorded about a device.</summary>
internal sealed record FingerprintRow(
    DeviceVendor Vendor,
    bool ReducedCapability,
    string? SysObjectId,
    string? SysDescr,
    string? SysName,
    double? UptimeSeconds,
    string? Model,
    string? OsVersion,
    string? SerialNumber,
    int InterfaceCount,
    bool InterfacesTruncated,
    IReadOnlyList<string> OverriddenFields,
    DateTimeOffset? LastWalkAt,
    Guid? LastAppliedJobId,
    string? LastError);

/// <summary>One row of a device's interface inventory.</summary>
internal sealed record InterfaceRow(
    int IfIndex,
    string? Name,
    string? Description,
    string? Alias,
    int? InterfaceType,
    int? Mtu,
    long? SpeedBitsPerSecond,
    string? PhysicalAddress,
    int? AdminStatus,
    int? OperStatus,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>The identity facts on the device row, and when that row last changed.</summary>
internal sealed record DeviceFacts(
    DeviceVendor Vendor,
    string? Model,
    string? OsVersion,
    string? SerialNumber,
    DateTimeOffset UpdatedAt);
