using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// The broker, RabbitMQ, as a container whose data outlives a restart on a named volume. In run mode it has its management
/// UI and the admin user and password Aspire generates, as it always had. Published (docs/adr/0005), it is a Container App
/// that never runs a second replica, without the management UI, with the admin user and password the deployment sets.
/// No service holds the admin's credentials (docs/adr/0011): every service connects as a user of its own, named after its
/// module, who may configure, write and read its own queue, the exchanges of its own module's events and the exchanges of
/// the events it consumes (<see cref="MessagingTopology"/>), and nothing else. The broker imports those users, the admin
/// and their permissions at every boot from a definitions document the AppHost composes, handed to it as the environment
/// variable <c>BROKER_DEFINITIONS</c>; published, that document and each service's connection string are Key Vault secrets.
/// </summary>
public static class Messaging
{
    private const string Name = "messaging";

    private const string DefinitionsVariable = "BROKER_DEFINITIONS";

    private const string VirtualHost = "/";

    /// <summary>
    /// The broker's entrypoint: it writes the definitions to the container's own layer (never the data volume), readable by
    /// RabbitMQ's user alone, has RabbitMQ load them at boot, drops the variable and starts the image's own entrypoint in
    /// its place. The image starts as root and its entrypoint runs RabbitMQ as the user rabbitmq.
    /// </summary>
    private const string Entrypoint = """
        set -eu
        umask 077
        printf '%s' "$BROKER_DEFINITIONS" > /tmp/rabbitmq-definitions.json
        printf 'load_definitions = /tmp/rabbitmq-definitions.json\n' > /etc/rabbitmq/conf.d/90-definitions.conf
        if [ "$(id -u)" = 0 ]; then chown rabbitmq:rabbitmq /tmp/rabbitmq-definitions.json /etc/rabbitmq/conf.d/90-definitions.conf; fi
        unset BROKER_DEFINITIONS
        exec docker-entrypoint.sh rabbitmq-server
        """;

    public static MessagingBroker AddMessaging(this IDistributedApplicationBuilder builder, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            var broker = builder.AddRabbitMQ(Name)
                .WithManagementPlugin()
                .WithDataVolume();
            // Each service's password is generated once and kept in the AppHost's user secrets, like the admin's, so the
            // users on the data volume keep theirs; letters and digits alone, so it needs no escaping in a URI or in JSON.
            var users = Users(builder, module => builder.AddParameter($"{Name}-{module}-password", new GenerateParameterDefault { MinLength = 22, Special = false }, secret: true, persist: true));
            var address = broker.Resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort);
            WithDefinitionsAtBoot(broker).WithEnvironment(DefinitionsVariable, Definitions(broker.Resource, users));
            return new MessagingBroker(
                broker,
                users.ToDictionary(
                    user => user.Module,
                    user => ReferenceExpression.Create($"amqp://{user.UserName.Resource:uri}:{user.Password.Resource:uri}@{address}"),
                    StringComparer.Ordinal));
        }

        var userName = builder.AddParameter($"{Name}-username");
        var password = secrets.Secret($"{Name}-password");
        var published = builder.AddRabbitMQ(Name, userName, password)
            .WithDataVolume()
            .WithSecret("RABBITMQ_DEFAULT_PASS", password, secrets)
            .WithSingleReplica();
        // Each service's password is a secret the deployment sets (messaging-<module>-password), kept in the Key Vault.
        var publishedUsers = Users(builder, module => secrets.Secret($"{Name}-{module}-password"));
        // The definitions hold every password, so the broker reads them as a Key Vault reference, as it reads its own.
        IAzureKeyVaultSecretReference definitions = secrets.Store($"{Name}-definitions", Definitions(published.Resource, publishedUsers)).Resource;
        WithDefinitionsAtBoot(published).WithEnvironment(DefinitionsVariable, definitions);
        // The Key Vault's Bicep cannot read a Container App's endpoint: given the broker's own connection string, it would
        // write amqp://user:password@ without host and port. A Container App with TCP ingress is reached inside the
        // environment by its name on its target port, which is what Aspire writes for a service that references the
        // broker; each service's secret names the same, from the model. Main.bicep does not URI-escape a value it
        // assembles, so every user and password on the broker must be URI-safe (docs/adr/0005, 0011). The admin's
        // connection string is not stored: no app reads it.
        var endpoint = published.Resource.PrimaryEndpoint.EndpointAnnotation;
        var publishedAddress = $"{published.Resource.Name}:{endpoint.TargetPort ?? throw new InvalidOperationException($"{Name}'s {endpoint.Name} endpoint has no target port.")}";
        foreach (var user in publishedUsers)
        {
            secrets.Store($"{Name}-{user.Module}-connection-string", ReferenceExpression.Create($"amqp://{user.UserName.Resource}:{user.Password.Resource}@{publishedAddress}"));
        }

        return new MessagingBroker(published, []);
    }

    /// <summary>
    /// The broker's connection string, <c>messaging</c>, of the module's own user, and the queue named after the module,
    /// for a service that waits for the broker: in run mode as a value, published as a Key Vault secret.
    /// </summary>
    public static IResourceBuilder<T> WithMessaging<T>(this IResourceBuilder<T> service, MessagingBroker broker, DeploymentSecrets secrets, string module)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        service.WithEnvironment("Messaging__Queue", module).WaitFor(broker.Resource);
        if (!secrets.IsPublishing)
        {
            return service.WithEnvironment($"ConnectionStrings__{Name}", broker.ConnectionStringOf(module));
        }

        IAzureKeyVaultSecretReference connectionString = secrets.Stored($"{Name}-{module}-connection-string").Resource;
        return service.WithEnvironment($"ConnectionStrings__{Name}", connectionString);
    }

    /// <summary>A user per service host, named after its module unless the deployment says otherwise, in ordinal order.</summary>
    private static List<BrokerUser> Users(IDistributedApplicationBuilder builder, Func<string, IResourceBuilder<ParameterResource>> password) =>
        [.. builder.ServiceHosts()
            .Select(service => service.Module)
            .Order(StringComparer.Ordinal)
            .Select(module => new BrokerUser(
                module,
                builder.AddParameter($"{Name}-{module}-username", module, publishValueAsDefault: true),
                password(module),
                MessagingTopology.Of(module)))];

    /// <summary>The broker's own entrypoint, which has RabbitMQ import <c>BROKER_DEFINITIONS</c> at every boot.</summary>
    private static IResourceBuilder<RabbitMQServerResource> WithDefinitionsAtBoot(IResourceBuilder<RabbitMQServerResource> broker) =>
        broker.WithEntrypoint("/bin/bash").WithArgs("-c", Entrypoint);

    /// <summary>
    /// The definitions RabbitMQ imports at boot: the vhost, the admin with every permission, and each service's user with
    /// its own (docs/adr/0011). Importing definitions, RabbitMQ seeds no default user, so the admin is in them too. The
    /// passwords go in as they are: generated letters and digits, or deployment values that must be URI-safe and so JSON-safe.
    /// </summary>
    private static ReferenceExpression Definitions(RabbitMQServerResource broker, List<BrokerUser> users)
    {
        var json = new ReferenceExpressionBuilder();
        Literal(json, $"{{\"vhosts\":[{{\"name\":{Json(VirtualHost)}}}],\"users\":[{{\"name\":\"");
        json.Append($"{broker.UserNameReference}");
        Literal(json, "\",\"password\":\"");
        json.Append($"{broker.PasswordParameter}");
        Literal(json, "\",\"tags\":[\"administrator\"]}");
        foreach (var user in users)
        {
            Literal(json, ",{\"name\":\"");
            json.Append($"{user.UserName.Resource}");
            Literal(json, "\",\"password\":\"");
            json.Append($"{user.Password.Resource}");
            Literal(json, "\",\"tags\":[]}");
        }

        Literal(json, "],\"permissions\":[{\"user\":\"");
        json.Append($"{broker.UserNameReference}");
        Literal(json, $"\",\"vhost\":{Json(VirtualHost)},\"configure\":\".*\",\"write\":\".*\",\"read\":\".*\"}}");
        foreach (var user in users)
        {
            var (consumed, published) = user.Topology;
            Literal(json, ",{\"user\":\"");
            json.Append($"{user.UserName.Resource}");
            Literal(json,
                $"\",\"vhost\":{Json(VirtualHost)}," +
                $"\"configure\":{Json(Pattern([user.Module, .. published, .. consumed]))}," +
                $"\"write\":{Json(Pattern([user.Module, .. published]))}," +
                $"\"read\":{Json(Pattern([user.Module, .. consumed]))}}}");
        }

        Literal(json, "]}");
        return json.Build();
    }

    /// <summary>
    /// Exactly the given queue and exchange names: RabbitMQ matches a permission's regular expression anywhere in a name,
    /// so it is anchored, and each name is escaped.
    /// </summary>
    private static string Pattern(IEnumerable<string> names) =>
        $"^({string.Join('|', names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(Regex.Escape))})$";

    private static string Json(string value) => JsonSerializer.Serialize(value);

    /// <summary>JSON text in the expression, whose format string reads braces as placeholders unless doubled.</summary>
    private static void Literal(ReferenceExpressionBuilder expression, string text) =>
        expression.AppendLiteral(text.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
}
