using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// The broker and, in run mode, the connection string of each service's own user, by module (<see cref="Messaging"/>).
/// Published, a service's connection string is the Key Vault secret <c>messaging-&lt;module&gt;-connection-string</c>.
/// </summary>
public sealed class MessagingBroker
{
    private readonly Dictionary<string, ReferenceExpression> connectionStrings;

    internal MessagingBroker(IResourceBuilder<RabbitMQServerResource> resource, Dictionary<string, ReferenceExpression> connectionStrings)
    {
        Resource = resource;
        this.connectionStrings = connectionStrings;
    }

    /// <summary>The broker's own resource, whose connection string is its admin's.</summary>
    public IResourceBuilder<RabbitMQServerResource> Resource { get; }

    /// <summary>The run-mode connection string of the module's own user.</summary>
    internal ReferenceExpression ConnectionStringOf(string module) =>
        connectionStrings.TryGetValue(module, out var connectionString)
            ? connectionString
            : throw new InvalidOperationException($"The broker has no user of the module '{module}'; AddMessaging creates one for every src/Services/*/Host/Host.csproj.");
}
