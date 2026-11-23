using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// The broker, the user name of each service's own user and, in run mode, its connection string, by module
/// (<see cref="Messaging"/>). Published, a service's connection string is the Key Vault secret
/// <c>messaging-&lt;module&gt;-connection-string</c>.
/// </summary>
public sealed class MessagingBroker
{
    private readonly Dictionary<string, ReferenceExpression> connectionStrings;

    internal MessagingBroker(
        IResourceBuilder<RabbitMQServerResource> resource,
        IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> userNames,
        Dictionary<string, ReferenceExpression> connectionStrings)
    {
        Resource = resource;
        UserNames = userNames;
        this.connectionStrings = connectionStrings;
    }

    /// <summary>The broker's own resource, whose connection string is its admin's.</summary>
    public IResourceBuilder<RabbitMQServerResource> Resource { get; }

    /// <summary>
    /// The user name of each service's own user (<c>messaging-&lt;module&gt;-username</c>), by module in ordinal order: the
    /// user RabbitMQ vouches for as a message's <c>user_id</c>, which tells a service whose module published it.
    /// </summary>
    internal IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> UserNames { get; }

    /// <summary>The run-mode connection string of the module's own user.</summary>
    internal ReferenceExpression ConnectionStringOf(string module) =>
        connectionStrings.TryGetValue(module, out var connectionString)
            ? connectionString
            : throw new InvalidOperationException($"The broker has no user of the module '{module}'; AddMessaging creates one for every src/Services/*/Host/Host.csproj.");
}
