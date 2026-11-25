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

        bicep.Module("secrets").ShouldContain("publicNetworkAccess: 'Disabled'");
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
}
