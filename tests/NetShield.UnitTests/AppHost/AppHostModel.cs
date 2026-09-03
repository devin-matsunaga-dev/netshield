using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace NetShield.UnitTests.AppHost;

/// <summary>
/// Builds the <c>NetShield.AppHost</c> resource model once for the whole test class.
/// Only the model is built — no container starts, nothing is pulled, and the tests stay
/// runnable on a machine with no Docker daemon (CONVENTIONS.md §7 reserves container-backed
/// testing for the integration suite).
/// </summary>
public sealed class AppHostModel : IAsyncLifetime
{
    private IDistributedApplicationTestingBuilder? builder;

    public IReadOnlyList<IResource> Resources =>
        builder?.Resources.ToList() ?? throw new InvalidOperationException("The model has not been built.");

    public IResource Resource(string name) =>
        Resources.SingleOrDefault(resource => resource.Name == name)
        ?? throw new InvalidOperationException($"NetShield.AppHost declares no resource named '{name}'.");

    /// <summary>
    /// Resolves the environment variables a container is started with. The replacement for the
    /// obsolete API is internal in Aspire 13.5.3, so the obsolete call is the only public route.
    /// It is safe here because container environments resolve without allocated endpoints;
    /// project resources do not, and are not asked.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> EnvironmentOf(ContainerResource container)
    {
#pragma warning disable CS0618 // ExecutionConfigurationBuilder, its replacement, is not public.
        return await container.GetEnvironmentVariableValuesAsync();
#pragma warning restore CS0618
    }

    /// <summary>
    /// The names of every environment variable a resource would be started with, including the
    /// ones Aspire contributes for a reference. The callbacks are run against a fresh context, so
    /// only the names are trustworthy here — a value may still be an unresolved placeholder.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnvironmentNamesOf(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        EnvironmentCallbackContext context = new(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            resource);

        foreach (EnvironmentCallbackAnnotation annotation in
            resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return [.. context.EnvironmentVariables.Keys];
    }

    public async ValueTask InitializeAsync() =>
        builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.NetShield_AppHost>(
            TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (builder is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }
}
