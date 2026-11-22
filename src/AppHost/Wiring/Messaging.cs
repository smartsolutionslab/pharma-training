using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// The broker, RabbitMQ, as a container whose data outlives a restart on a named volume. In run mode it has its management
/// UI and the user and password Aspire generates, as it always had. Published (docs/adr/0005), it is a Container App that
/// never runs a second replica, without the management UI, with the user and password the deployment sets; the password
/// and the connection string the services read are secrets in the Key Vault, which both sides read as Key Vault
/// references.
/// </summary>
public static class Messaging
{
    private const string Name = "messaging";

    public static IResourceBuilder<RabbitMQServerResource> AddMessaging(this IDistributedApplicationBuilder builder, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            return builder.AddRabbitMQ(Name)
                .WithManagementPlugin()
                .WithDataVolume();
        }

        var userName = builder.AddParameter($"{Name}-username");
        var password = secrets.Secret($"{Name}-password");
        var broker = builder.AddRabbitMQ(Name, userName, password)
            .WithDataVolume()
            .WithSecret("RABBITMQ_DEFAULT_PASS", password, secrets)
            .WithSingleReplica();
        // The Key Vault's Bicep cannot read a Container App's endpoint: given the broker's own connection string, it would
        // write amqp://user:password@ without host and port. A Container App with TCP ingress is reached inside the
        // environment by its name on its target port, which is what Aspire writes for a service that references the
        // broker; the secret names the same, from the model. Main.bicep does not URI-escape a value it assembles, so the
        // broker's user and password must be URI-safe (docs/adr/0005).
        var endpoint = broker.Resource.PrimaryEndpoint.EndpointAnnotation;
        var address = $"{broker.Resource.Name}:{endpoint.TargetPort ?? throw new InvalidOperationException($"{Name}'s {endpoint.Name} endpoint has no target port.")}";
        secrets.Store($"{Name}-connection-string", ReferenceExpression.Create($"amqp://{userName.Resource}:{password.Resource}@{address}"));
        return broker;
    }

    /// <summary>
    /// The broker's connection string, <c>messaging</c>, for a service that waits for the broker: in run mode as a
    /// reference, published as the Key Vault secret <see cref="AddMessaging"/> stored.
    /// </summary>
    public static IResourceBuilder<T> WithMessaging<T>(this IResourceBuilder<T> service, IResourceBuilder<RabbitMQServerResource> broker, DeploymentSecrets secrets)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            return service.WithReference(broker).WaitFor(broker);
        }

        IAzureKeyVaultSecretReference connectionString = secrets.Stored($"{Name}-connection-string").Resource;
        return service.WithEnvironment($"ConnectionStrings__{Name}", connectionString).WaitFor(broker);
    }
}
