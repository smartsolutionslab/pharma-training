using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// In run mode every container outlives the AppHost (<see cref="ContainerLifetime.Persistent"/>): a restart finds the
/// database, the broker, Keycloak, the storage emulator and Mailpit running and healthy instead of creating and booting
/// them again, Keycloak's boot alone taking about 15 seconds. Aspire creates a container again when its configuration
/// changes; <c>docker rm -f</c> or the dashboard's Stop removes one by hand. The integration tests start their own, which
/// end with the run (AspireFixture). Published, nothing changes.
/// </summary>
public static class ContainerLifetimes
{
    /// <summary>
    /// Keeps every container added so far running between runs; call it last, after every resource is added.
    /// </summary>
    public static IDistributedApplicationBuilder KeepContainersBetweenRuns(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.ExecutionContext.IsRunMode)
        {
            return builder;
        }

        foreach (var container in builder.Resources.Where(resource => resource.Annotations.OfType<ContainerImageAnnotation>().Any()).ToList())
        {
            builder.CreateResourceBuilder(container)
                .WithAnnotation(new ContainerLifetimeAnnotation { Lifetime = ContainerLifetime.Persistent }, ResourceAnnotationMutationBehavior.Replace);
        }

        return builder;
    }
}
