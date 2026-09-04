using Microsoft.Extensions.DependencyInjection;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Persistence;

using NetShield.Platform;
using NetShield.Platform.Persistence;

namespace NetShield.Web.Host;

/// <summary>
/// The key rotation step, run as <c>NetShield.Web.Host --rewrap</c>: move every stored credential
/// onto the active key-encryption key, write the audit row, and exit.
/// </summary>
/// <remarks>
/// <para>
/// A command rather than an endpoint. Rotating the key that wraps every credential in the system
/// is key management, not application traffic — a long-lived HTTP route would put the most
/// privileged cryptographic operation NetShield has permanently on the web attack surface, and
/// the only thing it would buy is the audit row <c>AuditMiddleware</c> writes for free.
/// <see cref="CredentialKeyRewrapper"/> writes that row itself instead, which is the cheaper half
/// of the trade; reducing exposure is the harder half and this is where it is bought.
/// </para>
/// <para>
/// Like the migration step, it is the same artifact with an argument and it binds no socket
/// (ARCHITECTURE.md §2 fixes the process model at five). It is safe to repeat: a second run over
/// an estate already on the active key reports nothing moved, which is how an operator knows the
/// key they are retiring is free to remove from the ring.
/// </para>
/// <para>
/// It is not run by the AppHost. Development generates one key and never rotates it; a rotation
/// is something an operator does deliberately, against a ring holding both the old key and the
/// new one.
/// </para>
/// </remarks>
internal static class RewrapMode
{
    /// <summary>The argument that selects this mode.</summary>
    internal const string Switch = "--rewrap";

    /// <summary>Whether the process was asked to rotate rather than to serve.</summary>
    internal static bool IsRequested(string[] args) =>
        args.Contains(Switch, StringComparer.Ordinal);

    /// <summary>Re-wraps everything and reports what moved.</summary>
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        // The switch is removed before configuration sees it: the command-line provider rejects a
        // bare `--rewrap`, which has no value to bind.
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            args.Where(argument => !string.Equals(argument, Switch, StringComparison.Ordinal)).ToArray());

        builder.AddServiceDefaults();

        // Two contexts, both without health checks: this process answers no probe. Platform is
        // here because audit_log lives on it, and a rotation that could not record itself would
        // be a privileged operation with no trace (SPEC.md §5).
        builder.AddNpgsqlDbContext<PlatformDbContext>(
            ConnectionNames.Database,
            settings => settings.DisableHealthChecks = true,
            options => options.UseNetShieldConventions());

        builder.AddNpgsqlDbContext<InventoryDbContext>(
            ConnectionNames.Database,
            settings => settings.DisableHealthChecks = true,
            options => options.UseInventoryConventions());

        builder.AddNetShieldPlatform();

        // The key ring, and nothing else of the Inventory module. No handlers, no validators, no
        // endpoints — this process needs to unwrap and re-wrap, and nothing more.
        builder.AddNetShieldEnvelopeEncryption();

        // The audit writer. AddNetShieldAudit registers the appender without adding the
        // middleware, which is exactly what a process with no request pipeline needs.
        builder.AddNetShieldAudit();

        builder.Services.AddScoped<CredentialKeyRewrapper>();

        using IHost host = builder.Build();

        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(RewrapMode));

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();

        CredentialRewrapReport report = await scope.ServiceProvider
            .GetRequiredService<CredentialKeyRewrapper>()
            .RewrapAsync(cancellationToken);

        logger.LogInformation(
            "Re-wrapped {Rewrapped} of {Examined} credential profiles onto key {ActiveKeyId}. "
            + "Keep every other key in the ring until a run reports none examined.",
            report.Rewrapped,
            report.Examined,
            report.ActiveKeyId);

        return 0;
    }
}
