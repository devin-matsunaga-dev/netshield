using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NetShield.Contracts.Identity;

using NetShield.Identity;
using NetShield.Identity.Endpoints;
using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;

using NetShield.Inventory;
using NetShield.Inventory.Endpoints;
using NetShield.Inventory.Persistence;

using NetShield.Platform;
using NetShield.Platform.Auditing;
using NetShield.Platform.Messaging;
using NetShield.Platform.Persistence;
using NetShield.Platform.Problems;

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
internal sealed class InventoryHost(WebApplication application, SessionClient client) : IAsyncDisposable
{
    /// <summary>The password every account this harness creates signs in with.</summary>
    internal const string Password = "Correct-Horse-Battery-9";

    /// <summary>The work factor the suite hashes at. The floor the options allow, for speed.</summary>
    private const string TestMemoryKib = "8192";

    /// <summary>The client, holding whatever cookies the API has set on it.</summary>
    public SessionClient Client => client;

    /// <summary>Where the host is listening, for a test that needs a client with no session.</summary>
    public Uri BaseAddress => new(application.Urls.First());

    /// <summary>Starts the host and signs in as <paramref name="role"/>.</summary>
    public static async Task<InventoryHost> StartAsync(
        PostgresFixture postgres,
        CancellationToken cancellationToken,
        UserRole role = UserRole.Administrator)
    {
        string connectionString = await postgres.CreateDatabaseAsync(cancellationToken);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(InventoryHost).Assembly.GetName().Name
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:PasswordHashing:MemoryKib"] = TestMemoryKib,
            ["Identity:PasswordHashing:Iterations"] = "1"
        });

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

        SessionClient client = new(new HttpClient { BaseAddress = new Uri(application.Urls.First()) });

        InventoryHost host = new(application, client);

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

    public async ValueTask DisposeAsync()
    {
        client.Dispose();

        await application.StopAsync();
        await application.DisposeAsync();
    }
}

/// <summary>One audit row, reduced to what these tests assert on.</summary>
internal sealed record AuditRow(string Action, string? TargetType, string? TargetId, AuditOutcome Outcome);
