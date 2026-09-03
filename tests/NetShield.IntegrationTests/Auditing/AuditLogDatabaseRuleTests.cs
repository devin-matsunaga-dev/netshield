using FluentAssertions;

using NetShield.Contracts.Identity;
using NetShield.IntegrationTests.Identity;
using NetShield.IntegrationTests.Platform;
using NetShield.Platform.Auditing;

using Npgsql;

namespace NetShield.IntegrationTests.Auditing;

/// <summary>
/// WP-0.5: <c>UPDATE audit_log</c> fails at the database.
/// </summary>
/// <remarks>
/// The code-level guard is enforced by <c>NetShield.ArchitectureTests</c>. This is the half that
/// still holds when someone opens <c>psql</c> — which is the half ARCHITECTURE.md §8 is actually
/// worried about.
/// </remarks>
public sealed class AuditLogDatabaseRuleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Username = "netadmin";
    private const string Password = "Correct-Horse-42";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_IsRefusedByTheDatabase()
    {
        await using IdentityHost host = await HostWithOneRow();

        await Attempt(host, "UPDATE audit_log SET action = 'nothing-happened'")
            .Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.MessageText.Contains("append-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_IsRefusedByTheDatabase()
    {
        await using IdentityHost host = await HostWithOneRow();

        await Attempt(host, "DELETE FROM audit_log").Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Truncate_IsRefusedByTheDatabase()
    {
        await using IdentityHost host = await HostWithOneRow();

        // TRUNCATE bypasses row-level triggers entirely, and is the fastest way to empty a table
        // for anyone who thinks to try it.
        await Attempt(host, "TRUNCATE audit_log").Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task AStatementMatchingNoRows_IsRefusedToo()
    {
        await using IdentityHost host = await HostWithOneRow();

        // The trigger is FOR EACH STATEMENT rather than FOR EACH ROW for exactly this: a DELETE
        // that silently succeeds because it matched nothing is a rule with a hole in it.
        await Attempt(host, "DELETE FROM audit_log WHERE id = '00000000-0000-0000-0000-000000000000'")
            .Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task ARefusedStatement_LeavesEveryRowWhereItWas()
    {
        await using IdentityHost host = await HostWithOneRow();

        IReadOnlyList<AuditEntry> before = await host.ReadAuditEntriesAsync(Token);

        await Attempt(host, "DELETE FROM audit_log").Should().ThrowAsync<PostgresException>();

        (await host.ReadAuditEntriesAsync(Token)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Insert_IsStillAllowed()
    {
        await using IdentityHost host = await HostWithOneRow();

        // Append-only means append, not read-only. The whole point is that the log keeps growing.
        await host.LoginAsync(Username, Password, Token);

        (await host.ReadAuditEntriesAsync(Token)).Should().HaveCount(2);
    }

    private static Func<Task> Attempt(IdentityHost host, string sql) =>
        () => host.ExecuteSqlAsync(sql, Token);

    private async Task<IdentityHost> HostWithOneRow()
    {
        IdentityHost host = await IdentityHost.StartAsync(postgres, Token);

        await host.CreateUserAsync(Username, Password, Token, UserRole.Administrator);
        await host.LoginAsync(Username, Password, Token);

        (await host.ReadAuditEntriesAsync(Token)).Should().ContainSingle();

        return host;
    }
}
