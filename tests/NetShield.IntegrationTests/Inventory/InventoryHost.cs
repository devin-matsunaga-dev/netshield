using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;

using NetShield.Identity;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory;
using NetShield.Inventory.Collector;
using NetShield.Inventory.Credentials;
using NetShield.Inventory.Endpoints;
using NetShield.Inventory.Persistence;

using NetShield.Platform;
using NetShield.Platform.Auditing;
using NetShield.Platform.Messaging;
using NetShield.Platform.Persistence;
using NetShield.Platform.Problems;
using NetShield.Platform.Results;

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
    public static async Task<InventoryHost> StartAsync(
        PostgresFixture postgres,
        CancellationToken cancellationToken,
        UserRole role = UserRole.Administrator,
        string? database = null,
        IReadOnlyList<(string Id, string Key)>? keyRing = null,
        string? activeKeyId = null,
        int leaseSeconds = 300,
        int maxAttempts = 3,
        string collectorSecret = CollectorSharedSecret)
    {
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
            ["Collector:Jobs:MaxAttempts"] = maxAttempts.ToString(CultureInfo.InvariantCulture)
        };

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

        builder.Services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString).UseNetShieldConventions());
        builder.Services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString).UseIdentityConventions());
        builder.Services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString).UseInventoryConventions());

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
                job.Result))
            .SingleAsync(cancellationToken);
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
internal sealed record CollectorJobRow(
    CollectorJobStatus Status,
    CollectorJobOutcome? Outcome,
    int Attempts,
    string? LeaseToken,
    string? LeasedBy,
    string? Detail,
    string? Result);

/// <summary>One collector's self-reported state.</summary>
internal sealed record CollectorNodeRow(string Name, string? Version, int Capacity, int Running);

/// <summary>The three columns a sealed credential occupies.</summary>
/// <param name="KeyId">Which key-encryption key the wrapped data key is under.</param>
/// <param name="WrappedDataKey">The data key, sealed.</param>
/// <param name="MaterialCiphertext">The material, sealed.</param>
internal sealed record StoredCiphertext(string KeyId, byte[] WrappedDataKey, byte[] MaterialCiphertext);
