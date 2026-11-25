using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.PostgreSql;

namespace Aspire.Hosting;

/// <summary>
/// Published, the network the application runs in (#149, docs/adr/0038): the module <c>network</c>, written by hand in
/// <c>network/network.bicep</c>, holds a virtual network whose subnet <c>aca-subnet</c> the Container Apps environment is
/// injected into, and a private endpoint for the PostgreSQL server and one for the Key Vault in its subnet
/// <c>private-endpoints</c>, with the private DNS zones their names resolve in. Neither resource has a public endpoint,
/// and the server has no firewall rule, Aspire's <c>AllowAllAzureIps</c> included. The services reach the storage
/// account's blobs through a private endpoint as well, while its public endpoint stays for the browsers (the owner's
/// decision (a) on #149). Nothing in run mode.
/// Aspire 13.6.5's own networking API (Aspire.Hosting.Azure.Network, ASPIREAZURE003) and Azure.Provisioning's private DNS
/// types (AZPROVISION001) are for evaluation only and compile only with the diagnostic silenced, so the network is a Bicep
/// template, which Aspire adds and wires as it does any other. The environment's apps stay reachable from the internet
/// through its ingress; Blob Storage keeps its public endpoint, because browsers upload to it and read from it with the
/// SAS URLs the services sign (docs/adr/0002).
/// </summary>
public static class PrivateNetwork
{
    private const string Name = "network";

    private const string Template = "network/network.bicep";

    private const string BlobEndpointName = "blob-endpoint";

    private const string BlobEndpointTemplate = "network/blob-endpoint.bicep";

    private const string SubnetOutput = "acaSubnetId";

    private const string SubnetParameter = "network_outputs_acasubnetid";

    /// <summary>
    /// Adds the network for the server, the vault and the blobs, and gives the environment its subnet; publish mode only.
    /// </summary>
    public static IDistributedApplicationBuilder AddPrivateNetwork(
        this IDistributedApplicationBuilder builder,
        DeploymentSecrets secrets,
        IResourceBuilder<IResourceWithConnectionString> databaseServer,
        IResourceBuilder<AzureStorageResource> storage)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(databaseServer);
        ArgumentNullException.ThrowIfNull(storage);

        if (!secrets.IsPublishing)
        {
            return builder;
        }

        if (databaseServer.Resource is not AzurePostgresFlexibleServerResource server)
        {
            throw new InvalidOperationException($"{databaseServer.Resource.Name} is not the Flexible Server; the private network exists once published alone.");
        }

        var network = builder.AddBicepTemplate(Name, Template)
            .WithParameter("postgres_outputs_id", new BicepOutputReference("id", server))
            .WithParameter("secrets_outputs_id", new BicepOutputReference("id", secrets.Vault.Resource));

        // A module of its own: the account comes after the environment, which comes after the network, so the network
        // cannot wait for the account (docs/adr/0038).
        builder.AddBicepTemplate(BlobEndpointName, BlobEndpointTemplate)
            .WithParameter("storage_outputs_id", new BicepOutputReference("id", storage.Resource))
            .WithParameter("network_outputs_privateendpointssubnetid", network.GetOutput("privateEndpointsSubnetId"))
            .WithParameter("network_outputs_blobzoneid", network.GetOutput("blobZoneId"));

        builder.CreateResourceBuilder(builder.Resources.OfType<AzureContainerAppEnvironmentResource>().Single())
            .WithParameter(SubnetParameter, network.GetOutput(SubnetOutput));
        return builder;
    }

    /// <summary>Injects the environment into the network's subnet <c>aca-subnet</c>; its ingress stays public.</summary>
    public static void InjectInto(AzureResourceInfrastructure infrastructure, ContainerAppManagedEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(environment);

        var subnet = new ProvisioningParameter(SubnetParameter, typeof(string));
        infrastructure.Add(subnet);
        environment.VnetConfiguration = new ContainerAppVnetConfiguration
        {
            InfrastructureSubnetId = subnet,
            IsInternal = false,
        };
    }

    /// <summary>Takes the server's public endpoint and every firewall rule away, Aspire's <c>AllowAllAzureIps</c> included.</summary>
    public static void DenyPublicAccess(AzureResourceInfrastructure infrastructure, PostgreSqlFlexibleServer server)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(server);

        server.Network = new PostgreSqlFlexibleServerNetwork { PublicNetworkAccess = PostgreSqlFlexibleServerPublicNetworkAccessState.Disabled };
        foreach (var rule in infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServerFirewallRule>().ToList())
        {
            infrastructure.Remove(rule);
        }
    }

    /// <summary>Takes the vault's public endpoint away: it answers through its private endpoint alone.</summary>
    public static void DenyPublicAccess(KeyVaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        vault.Properties.PublicNetworkAccess = "Disabled";
        vault.Properties.NetworkRuleSet = new KeyVaultNetworkRuleSet
        {
            DefaultAction = KeyVaultNetworkRuleAction.Deny,
            Bypass = KeyVaultNetworkRuleBypassOption.None,
        };
    }
}
