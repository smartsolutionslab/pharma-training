using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Published, the Container Apps environment runs in a virtual network of its own, and PostgreSQL and the Key Vault are
/// reached through private endpoints in it alone: neither has a public endpoint, and the server has no firewall rule
/// letting in every Azure address. The services reach Blob Storage through a private endpoint too, while its public
/// endpoint stays for the browsers' SAS uploads and reads (#149, the owner's decision (a); docs/adr/0038).
/// </summary>
public class AppHostPrivateNetworkTests
{
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_database_server_has_no_public_endpoint_and_no_rule_for_every_azure_address(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var postgres = bicep.Module("postgres");

        postgres.ShouldNotContain("AllowAllAzureIps");
        postgres.ShouldNotContain("'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@");
        postgres.ShouldContain("publicNetworkAccess: 'Disabled'");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_key_vault_has_no_public_endpoint(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var secrets = bicep.Module("secrets");

        secrets.ShouldContain("publicNetworkAccess: 'Disabled'");
        // Denied by default, and no trusted service passes: Container Apps is not one, so a bypass would not help a Key Vault
        // reference, and the deployment's own writes go through Azure Resource Manager (docs/adr/0038).
        secrets.ShouldContain("defaultAction: 'Deny'");
        secrets.ShouldContain("bypass: 'None'");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_container_apps_environment_runs_in_a_subnet_of_the_virtual_network(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var aca = bicep.Module("aca");

        aca.ShouldContain("vnetConfiguration:");
        aca.ShouldContain("infrastructureSubnetId:");
        bicep.Files.Values.ShouldContain(
            file => file.Contains("'Microsoft.Network/virtualNetworks@", StringComparison.Ordinal),
            "The published Bicep should declare the virtual network the environment runs in.");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging, "postgresqlServer")]
    [InlineData(PublishedAppHost.Staging, "vault")]
    [InlineData(PublishedAppHost.Production, "postgresqlServer")]
    [InlineData(PublishedAppHost.Production, "vault")]
    [InlineData(PublishedAppHost.Staging, "blob")]
    [InlineData(PublishedAppHost.Production, "blob")]
    public async Task The_server_the_vault_and_the_blobs_are_reached_through_a_private_endpoint(string environment, string groupId)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var endpoints = bicep.Files.Values
            .Where(file => file.Contains("'Microsoft.Network/privateEndpoints@", StringComparison.Ordinal))
            .ToList();

        endpoints.ShouldContain(
            file => file.Contains($"'{groupId}'", StringComparison.Ordinal),
            $"No private endpoint in the published Bicep connects to the group '{groupId}'.");
        bicep.Files.Values.ShouldContain(
            file => file.Contains("'Microsoft.Network/privateDnsZones@", StringComparison.Ordinal),
            "A private endpoint needs a private DNS zone, or the apps resolve the public address.");
    }

    // Browsers upload to and read from the account directly with the SAS URLs the services sign (docs/adr/0002), so its
    // public endpoint stays: the services' own traffic goes through the private endpoint.
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_storage_account_keeps_its_public_endpoint_for_the_browsers(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var storage = bicep.Module("storage");

        storage.ShouldNotContain("publicNetworkAccess: 'Disabled'");
        storage.ShouldNotContain("defaultAction: 'Deny'");
    }

    // Each module that reaches a resource privately is handed that resource's own id, and the environment the network's
    // subnet: a miswired id would put an endpoint on the wrong resource, or none at all (#415).
    [Theory]
    [InlineData(PublishedAppHost.Staging, "network", "postgres_outputs_id", "postgres.outputs.id")]
    [InlineData(PublishedAppHost.Staging, "network", "secrets_outputs_id", "secrets.outputs.id")]
    [InlineData(PublishedAppHost.Staging, "blob_endpoint", "storage_outputs_id", "storage.outputs.id")]
    [InlineData(PublishedAppHost.Staging, "blob_endpoint", "network_outputs_privateendpointssubnetid", "network.outputs.privateEndpointsSubnetId")]
    [InlineData(PublishedAppHost.Staging, "blob_endpoint", "network_outputs_blobzoneid", "network.outputs.blobZoneId")]
    [InlineData(PublishedAppHost.Staging, "aca", "network_outputs_acasubnetid", "network.outputs.acaSubnetId")]
    [InlineData(PublishedAppHost.Production, "network", "postgres_outputs_id", "postgres.outputs.id")]
    [InlineData(PublishedAppHost.Production, "network", "secrets_outputs_id", "secrets.outputs.id")]
    [InlineData(PublishedAppHost.Production, "blob_endpoint", "storage_outputs_id", "storage.outputs.id")]
    [InlineData(PublishedAppHost.Production, "aca", "network_outputs_acasubnetid", "network.outputs.acaSubnetId")]
    public async Task Main_bicep_hands_each_network_module_the_right_ids(string environment, string module, string parameter, string value)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        var block = Regex.Match(bicep.Files["main.bicep"], $@"(?ms)^module\s+{Regex.Escape(module)}\s+'[^']+'\s*=\s*\{{.*?^\}}", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;

        block.ShouldNotBeEmpty($"main.bicep has no module {module}.");
        block.ShouldContain($"{parameter}: {value}", Case.Sensitive, $"main.bicep's module {module} does not hand {parameter} the value {value}: {block}");
    }

    // Subnets looked up by name, never by their position in the list, so reordering them cannot swap them (#415).
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_network_finds_its_subnets_by_name(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        bicep.Module("network").ShouldNotContain("subnets[");
    }

    // The blobs' zone holds a record for this account alone; another account's blob host, private endpoint or not, must
    // still resolve from inside the network, through public DNS (#415).
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_blob_zone_falls_back_to_public_dns(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        bicep.Module("network").ShouldContain("resolutionPolicy: 'NxDomainRedirect'");
    }
}
