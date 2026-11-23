using System.Text.Json.Nodes;
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
/// variable <c>BROKER_DEFINITIONS</c>. The document carries no user name and no password: each is the placeholder
/// <c>@@&lt;VARIABLE&gt;@@</c> of a variable of the broker's own (<c>RABBITMQ_DEFAULT_USER</c> and <c>RABBITMQ_DEFAULT_PASS</c>
/// for the admin, <c>BROKER_USER_&lt;MODULE&gt;</c> and <c>BROKER_PASSWORD_&lt;MODULE&gt;</c> for each service), which the
/// entrypoint fills in and checks. Published, every password and each service's connection string are Key Vault secrets;
/// the document is a plain value.
/// </summary>
public static partial class Messaging
{
    private const string Name = "messaging";

    private const string DefinitionsVariable = "BROKER_DEFINITIONS";

    private const string VirtualHost = "/";

    private const string AdminUserVariable = "RABBITMQ_DEFAULT_USER";

    private const string AdminPasswordVariable = "RABBITMQ_DEFAULT_PASS";

    private const string UserVariablePrefix = "BROKER_USER_";

    private const string PasswordVariablePrefix = "BROKER_PASSWORD_";

    /// <summary>
    /// The broker's entrypoint. It fills in each placeholder <c>@@&lt;VARIABLE&gt;@@</c> it finds in the definitions, the
    /// document's own, not the variables that happen to be set, and fails closed: the boot fails, naming the variable and
    /// never printing its value, when a placeholder names a variable that is not one of the broker's user or password
    /// variables, or one that is not set, empty or holds a character outside <c>[A-Za-z0-9._~-]</c> (URI-safe, as the
    /// connection strings need, and needing no escape in JSON; spelled out, so no locale widens it), when two user
    /// variables name the same user, or when anything that looks like a placeholder is left. No value holds <c>@</c>, so no
    /// value can bring in another placeholder. It writes the document to the container's own layer (never the data
    /// volume), readable by RabbitMQ's user alone, has RabbitMQ load it at boot, and starts the image's own entrypoint in its
    /// place without the admin's password or any <c>BROKER_*</c> variable, as <c>env</c> lists them: bash does not see one
    /// whose name is not a shell identifier, so it could not unset it, yet would hand it on. The image starts as root and
    /// its entrypoint runs RabbitMQ as the user rabbitmq.
    /// </summary>
    private const string Entrypoint = """
        set -eu
        umask 077
        placeholder='@@([A-Za-z0-9_]+)@@'
        safe='^[ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._~-]+$'
        refuse() { printf '%s\n' "$1" >&2; exit 1; }
        declare -A users=()
        definitions="$BROKER_DEFINITIONS"
        while [[ "$definitions" =~ $placeholder ]]; do
          variable="${BASH_REMATCH[1]}"
          case "$variable" in
            RABBITMQ_DEFAULT_USER|BROKER_USER_?*|RABBITMQ_DEFAULT_PASS|BROKER_PASSWORD_?*) ;;
            *) refuse "The broker definitions hold the placeholder of $variable, which is not one of the broker's user or password variables.";;
          esac
          [ -n "${!variable+set}" ] || refuse "$variable is not set; the broker definitions need it."
          value="${!variable}"
          [ -n "$value" ] || refuse "$variable is empty; the broker definitions need a value."
          [[ "$value" =~ $safe ]] || refuse "$variable holds a character other than A-Z, a-z, 0-9, '.', '_', '~' and '-', which the broker definitions refuse."
          case "$variable" in
            RABBITMQ_DEFAULT_USER|BROKER_USER_*)
              [ -z "${users[$value]+set}" ] || refuse "${users[$value]} and $variable name the same broker user."
              users[$value]="$variable";;
          esac
          definitions="${definitions//"@@$variable@@"/"$value"}"
        done
        unset value users
        case "$definitions" in *@@*) refuse "The broker definitions hold a placeholder that names no variable; a placeholder is @@<VARIABLE>@@ of letters, digits and _.";; esac
        rm -f /tmp/rabbitmq-definitions.json
        printf '%s' "$definitions" > /tmp/rabbitmq-definitions.json
        unset definitions
        printf 'load_definitions = /tmp/rabbitmq-definitions.json\n' > /etc/rabbitmq/conf.d/90-definitions.conf
        if [ "$(id -u)" = 0 ]; then chown rabbitmq:rabbitmq /tmp/rabbitmq-definitions.json /etc/rabbitmq/conf.d/90-definitions.conf; fi
        unset RABBITMQ_DEFAULT_PASS
        drop=()
        while IFS= read -r -d '' entry; do
          case "${entry%%=*}" in BROKER_*) drop+=(-u "${entry%%=*}");; esac
        done < <(env -0)
        exec env "${drop[@]}" docker-entrypoint.sh rabbitmq-server
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
            WithDefinitionsAtBoot(broker).WithEnvironment(DefinitionsVariable, Definitions(users));
            foreach (var user in users)
            {
                broker.WithEnvironment(UserVariable(user.Module), user.Module).WithEnvironment(PasswordVariable(user.Module), user.Password);
            }

            // The module is URI-safe (Users checks it), so it goes in as it is.
            return new MessagingBroker(
                broker,
                UserNames(users),
                users.ToDictionary(
                    user => user.Module,
                    user => ReferenceExpression.Create($"amqp://{user.Module}:{user.Password.Resource:uri}@{address}"),
                    StringComparer.Ordinal));
        }

        var userName = builder.AddParameter($"{Name}-username");
        var password = secrets.Secret($"{Name}-password");
        var published = builder.AddRabbitMQ(Name, userName, password)
            .WithDataVolume()
            .WithSecret(AdminPasswordVariable, password, secrets)
            .WithSingleReplica();
        // Each service's password is a secret the deployment sets (messaging-<module>-password), kept in the Key Vault.
        var publishedUsers = Users(builder, module => secrets.Secret($"{Name}-{module}-password"));
        // The definitions hold no password, only placeholders, so they are a plain value; the broker reads each password
        // as a Key Vault reference of its own. Stored in the vault, the document would get a Bicep parameter named after
        // the whole expression, longer than Bicep allows (BCP024).
        WithDefinitionsAtBoot(published).WithEnvironment(DefinitionsVariable, Definitions(publishedUsers));
        foreach (var user in publishedUsers)
        {
            published.WithEnvironment(UserVariable(user.Module), user.Module).WithSecret(PasswordVariable(user.Module), user.Password, secrets);
        }
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
            secrets.Store($"{Name}-{user.Module}-connection-string", ReferenceExpression.Create($"amqp://{user.Module}:{user.Password.Resource}@{publishedAddress}"));
        }

        return new MessagingBroker(published, UserNames(publishedUsers), []);
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
        // Every module's user name, so the service knows whose events a message's user_id vouches for (docs/adr/0011):
        // RabbitMQ refuses a user_id other than the connection's, and the service refuses an event of a module other than
        // the one whose user sent it. User names are no secret.
        foreach (var (publisher, userName) in broker.UserNames)
        {
            service.WithEnvironment($"Messaging__Publishers__{publisher}", userName);
        }

        if (!secrets.IsPublishing)
        {
            return service.WithEnvironment($"ConnectionStrings__{Name}", broker.ConnectionStringOf(module));
        }

        IAzureKeyVaultSecretReference connectionString = secrets.Stored($"{Name}-{module}-connection-string").Resource;
        return service.WithEnvironment($"ConnectionStrings__{Name}", connectionString);
    }

    /// <summary>The broker user of each module, by module: the module itself, since every user is named after its module.</summary>
    private static SortedDictionary<string, string> UserNames(List<BrokerUser> users) =>
        new(users.ToDictionary(user => user.Module, user => user.Module, StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>
    /// A user per service host, named after its module, in ordinal order. No parameter overrides the name (docs/adr/0011): an
    /// overridden name could equal another user's and take over that user's definitions entry. A module whose name is not
    /// URI-safe, or two modules whose variables would be one, stop the AppHost here rather than the broker at its boot.
    /// </summary>
    private static List<BrokerUser> Users(IDistributedApplicationBuilder builder, Func<string, IResourceBuilder<ParameterResource>> password)
    {
        var modules = builder.ServiceHosts().Select(service => service.Module).Order(StringComparer.Ordinal).ToList();
        foreach (var module in modules.Where(module => !UriSafe().IsMatch(module)))
        {
            throw new InvalidOperationException($"The module '{module}' cannot name a broker user: a user name is letters, digits, '.', '_', '~' and '-' alone (docs/adr/0011).");
        }

        foreach (var shared in modules.GroupBy(PasswordVariable, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            throw new InvalidOperationException($"The modules {string.Join(" and ", shared)} would share the broker variables {shared.Key} and {UserVariable(shared.First())}.");
        }

        return [.. modules.Select(module => new BrokerUser(module, password(module), MessagingTopology.Of(module)))];
    }

    /// <summary>
    /// The broker's variable that holds a service user's password: the module in upper case, any character but an ASCII
    /// letter or digit as <c>_</c>, so it is a shell variable's name.
    /// </summary>
    private static string PasswordVariable(string module) => PasswordVariablePrefix + VariableSuffix(module);

    /// <summary>The broker's variable that holds a service user's name, spelled as its password's.</summary>
    private static string UserVariable(string module) => UserVariablePrefix + VariableSuffix(module);

    private static string VariableSuffix(string module) =>
        string.Concat(module.ToUpperInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));

    /// <summary>The text that stands for a variable's value in the definitions until the broker's entrypoint fills it in.</summary>
    private static string Placeholder(string variable) => $"@@{variable}@@";

    /// <summary>The broker's own entrypoint, which has RabbitMQ import <c>BROKER_DEFINITIONS</c> at every boot.</summary>
    private static IResourceBuilder<RabbitMQServerResource> WithDefinitionsAtBoot(IResourceBuilder<RabbitMQServerResource> broker) =>
        broker.WithEntrypoint("/bin/bash").WithArgs("-c", Entrypoint);

    /// <summary>
    /// The definitions RabbitMQ imports at boot: the vhost, the admin with every permission, and each service's user with
    /// its own (docs/adr/0011). Importing definitions, RabbitMQ seeds no default user, so the admin is in them too. No user
    /// name or password goes in: each is the placeholder of the broker's variable that holds it, which the entrypoint fills
    /// in and checks, so the document is the same text in both modes and for every deployment.
    /// </summary>
    private static string Definitions(List<BrokerUser> users)
    {
        var admin = Placeholder(AdminUserVariable);
        JsonArray accounts = [new JsonObject { ["name"] = admin, ["password"] = Placeholder(AdminPasswordVariable), ["tags"] = new JsonArray("administrator") }];
        JsonArray permissions = [Permission(admin, ".*", ".*", ".*")];
        foreach (var user in users)
        {
            var name = Placeholder(UserVariable(user.Module));
            var (consumed, published) = user.Topology;
            accounts.Add(new JsonObject { ["name"] = name, ["password"] = Placeholder(PasswordVariable(user.Module)), ["tags"] = new JsonArray() });
            permissions.Add(Permission(
                name,
                Pattern([user.Module, .. published, .. consumed]),
                Pattern([user.Module, .. published]),
                Pattern([user.Module, .. consumed])));
        }

        return new JsonObject
        {
            ["vhosts"] = new JsonArray(new JsonObject { ["name"] = VirtualHost }),
            ["users"] = accounts,
            ["permissions"] = permissions,
        }.ToJsonString();
    }

    private static JsonObject Permission(string user, string configure, string write, string read) => new()
    {
        ["user"] = user,
        ["vhost"] = VirtualHost,
        ["configure"] = configure,
        ["write"] = write,
        ["read"] = read,
    };

    /// <summary>
    /// Exactly the given queue and exchange names: RabbitMQ matches a permission's regular expression anywhere in a name,
    /// so it is anchored, and each name is escaped.
    /// </summary>
    private static string Pattern(IEnumerable<string> names) =>
        $"^({string.Join('|', names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(Regex.Escape))})$";

    /// <summary>What a broker user's name and password may hold: URI-safe, and nothing JSON escapes (docs/adr/0011).</summary>
    [GeneratedRegex(@"\A[A-Za-z0-9._~-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex UriSafe();
}
