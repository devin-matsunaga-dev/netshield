using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;

using NetShield.Identity;
using NetShield.Identity.Authentication;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.IntegrationTests.Authorization;
using NetShield.IntegrationTests.Platform;

using NetShield.Platform;
using NetShield.Platform.Auditing;
using NetShield.Platform.Persistence;
using NetShield.Platform.Problems;

namespace NetShield.IntegrationTests.Identity;

/// <summary>
/// A host wired the way <c>NetShield.Web.Host</c> wires identity — problem details first,
/// authentication, authorization, the endpoint group — against a database of its own on a real
/// loopback port.
/// </summary>
/// <remarks>
/// CONVENTIONS.md §7 admits no in-memory provider. A unique index, a cascade delete and a
/// transaction that rolls back only behave like themselves on the real thing, and every one of
/// them is load-bearing here.
/// </remarks>
internal sealed class IdentityHost(
    WebApplication application,
    SessionClient client,
    TestTimeProvider time,
    RecordingLoggerProvider logs) : IAsyncDisposable
{
    /// <summary>The work factor the suite hashes at. The floor the options allow, for speed.</summary>
    private const string TestMemoryKib = "8192";

    /// <summary>The client, holding whatever cookies the API has set on it.</summary>
    public SessionClient Client => client;

    /// <summary>The clock every handler reads.</summary>
    public TestTimeProvider Time => time;

    /// <summary>Everything written to the log, at every level.</summary>
    public IReadOnlyList<RecordedLog> Logs => logs.Records;

    public static async Task<IdentityHost> StartAsync(
        PostgresFixture postgres,
        CancellationToken cancellationToken,
        string? seedPassword = null,
        bool applyMigrations = true)
    {
        string connectionString = await postgres.CreateDatabaseAsync(cancellationToken);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(IdentityHost).Assembly.GetName().Name
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        Dictionary<string, string?> settings = new()
        {
            ["Identity:PasswordHashing:MemoryKib"] = TestMemoryKib,
            ["Identity:PasswordHashing:Iterations"] = "1"
        };

        if (seedPassword is not null)
        {
            settings["Identity:Seed:Password"] = seedPassword;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        // Everything is recorded, so that "no password at any level" is a claim a test can check
        // rather than one the default filters would have made true by accident.
        RecordingLoggerProvider logs = new();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddSingleton<ILoggerProvider>(logs);

        TestTimeProvider time = new();
        builder.Services.AddSingleton<TimeProvider>(time);

        builder.Services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString).UseIdentityConventions());

        // Registered as NetShield.Web.Host registers it, so that the platform services this host
        // resolves are the ones the real composition root resolves. Nothing here dispatches the
        // outbox, so its table is never read.
        builder.Services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString).UseNetShieldConventions());

        builder.AddNetShieldPlatform();
        builder.Services.AddNetShieldProblemDetails();
        builder.AddNetShieldAuthorization();
        builder.AddNetShieldAudit();
        builder.AddNetShieldIdentity();

        WebApplication application = builder.Build();

        // Applied before the host starts, because the first-run seeder runs as the host starts and
        // there is nothing yet to seed into. NetShield.Web.Host does not migrate on startup — see
        // STATUS.md; this database belongs to the test, and the test migrates it.
        if (applyMigrations)
        {
            await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Database.MigrateAsync(cancellationToken);

            // audit_log and its append-only trigger belong to the platform context. Both
            // contexts migrate here for the same reason: NetShield.Web.Host does not migrate on
            // startup, so the database belongs to whoever created it.
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
                .Database.MigrateAsync(cancellationToken);
        }

        application.UseNetShieldProblemDetails();
        application.UseAuthentication();
        application.UseNetShieldAudit();
        application.UseAuthorization();
        application.MapIdentityEndpoints();
        application.MapProbeEndpoints();

        await application.StartAsync(cancellationToken);

        SessionClient client = new(new HttpClient { BaseAddress = new Uri(application.Urls.First()) });

        return new IdentityHost(application, client, time, logs);
    }

    /// <summary>Adds an account directly, the way a later administration package would.</summary>
    public async Task<Guid> CreateUserAsync(
        string username,
        string password,
        CancellationToken cancellationToken,
        UserRole role = UserRole.Administrator,
        bool mustChangePassword = false)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        DateTimeOffset now = time.GetUtcNow();

        User user = new()
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            NormalizedUsername = UserName.Normalize(username),
            DisplayName = username,
            PasswordHash = await hasher.HashAsync(password, cancellationToken),
            Role = role,
            MustChangePassword = mustChangePassword,
            IsActive = true,
            PasswordChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    /// <summary>Disables an account, the way a later administration package would.</summary>
    public async Task DisableAsync(string username, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        IdentityDbContext database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        string normalized = UserName.Normalize(username);

        User user = await database.Users.SingleAsync(
            candidate => candidate.NormalizedUsername == normalized,
            cancellationToken);

        user.IsActive = false;

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Reads an account back, so a test can assert on lockout state.</summary>
    public async Task<User> ReadUserAsync(string username, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        string normalized = UserName.Normalize(username);

        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.AsNoTracking()
            .SingleAsync(user => user.NormalizedUsername == normalized, cancellationToken);
    }

    /// <summary>Every account, so the seeder's behaviour can be asserted.</summary>
    public async Task<IReadOnlyList<User>> ReadUsersAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Every refresh token issued, newest last.</summary>
    public async Task<IReadOnlyList<RefreshToken>> ReadRefreshTokensAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .RefreshTokens.AsNoTracking()
            .OrderBy(token => token.CreatedAt).ThenBy(token => token.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Signs in over HTTP, leaving the cookies on <see cref="Client"/>.</summary>
    public Task<ApiResponse> LoginAsync(string username, string password, CancellationToken cancellationToken) =>
        client.PostAsync(
            $"{AuthenticationEndpoints.RoutePrefix}/login",
            new LoginRequest(username, password),
            cancellationToken);

    /// <summary>Every audit row written so far, oldest first.</summary>
    /// <remarks>
    /// Read through <c>Set&lt;AuditEntry&gt;()</c> because <c>PlatformDbContext</c> deliberately
    /// exposes no <c>DbSet</c> for the table — see the comment on the context.
    /// </remarks>
    public async Task<IReadOnlyList<AuditEntry>> ReadAuditEntriesAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .Set<AuditEntry>().AsNoTracking()
            .OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Runs raw SQL against the test database, so a test can try what the database is supposed
    /// to refuse.
    /// </summary>
    public async Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await application.DisposeAsync();
    }
}
