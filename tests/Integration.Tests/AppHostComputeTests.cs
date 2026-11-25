using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.AppContainers;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What runs in the Container Apps environment: the edge, every service, Keycloak, the auth proxy in front of it and the
/// broker, each a Container App with one replica always running; only the edge and the auth proxy are reachable from
/// outside, and the Aspire dashboard is not deployed.
/// </summary>
public class AppHostComputeTests
{
    // Staging's Mailpit answers outside too: its inbox, behind basic auth, is what the staging end-to-end run reads (#130).
    [Theory]
    [InlineData(PublishedAppHost.Production, new[] { "auth", "host" })]
    [InlineData(PublishedAppHost.Staging, new[] { "auth", "host", "mailpit" })]
    public async Task Only_the_edge_and_the_auth_proxy_are_external_and_on_staging_mailpits_inbox(string environment, string[] expected)
    {
        await using var builder = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);

        var external = builder.Resources
            .Where(resource => resource.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.IsExternal))
            .Select(resource => resource.Name)
            .Order(StringComparer.Ordinal);

        external.ShouldBe(expected, ignoreOrder: false, $"Published to {environment}, only these should be reachable from outside.");
    }

    [Fact]
    public async Task The_edge_every_service_keycloak_the_auth_proxy_and_the_broker_are_container_apps()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var expected = PublishedAppHost.Services(builder).Select(service => service.Name).Concat(["auth", "host", "keycloak", "messaging"]);

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        expected.Except(bicep.ContainerApps.Keys).ShouldBeEmpty("These are not published as Container Apps.");
    }

    [Fact]
    public async Task Every_container_app_keeps_one_replica()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        bicep.ContainerApps.ShouldNotBeEmpty();
        foreach (var (name, module) in bicep.ContainerApps)
        {
            PublishedBicep.Block(module, "scale").ShouldMatch(@"minReplicas:\s*1\b", $"{name} may scale to zero.");
        }
    }

    [Theory]
    [InlineData("keycloak")]
    [InlineData("messaging")]
    public async Task Keycloak_and_the_broker_never_run_a_second_replica(string resource)
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        bicep.ContainerApps.ShouldContainKey(resource);
        PublishedBicep.Block(bicep.ContainerApps[resource], "scale").ShouldMatch(@"maxReplicas:\s*1\b");
    }

    [Fact]
    public async Task The_dashboard_is_not_deployed()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        builder.Resources.OfType<AzureContainerAppEnvironmentResource>().ShouldHaveSingleItem();

        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        bicep.Module("aca").ShouldNotContain("aspire-dashboard");
    }
}
