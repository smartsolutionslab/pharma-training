using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Where the published AppHost runs: one Azure Container Apps environment, <c>aca</c>, in publish mode alone
/// (docs/adr/0004). Its Aspire dashboard is not deployed: it would show logs and traces, personal data among them, on a
/// public URL; they stay in Log Analytics. No region is named here; the deployment chooses it.
/// </summary>
public static class ContainerAppsEnvironment
{
    private const string Name = "aca";

    public static IDistributedApplicationBuilder AddContainerAppsEnvironment(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.ExecutionContext.IsPublishMode)
        {
            builder.AddAzureContainerAppEnvironment(Name).WithDashboard(false);
        }

        return builder;
    }

    /// <summary>
    /// A container that must never run twice at once (the broker, Keycloak with its caches): its Container App keeps one
    /// replica, never more. Every Container App keeps at least one, so nothing scales to zero.
    /// </summary>
    public static IResourceBuilder<T> WithSingleReplica<T>(this IResourceBuilder<T> container)
        where T : ContainerResource
    {
        ArgumentNullException.ThrowIfNull(container);

        return container.PublishAsAzureContainerApp((_, app) =>
        {
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 1;
        });
    }
}
