using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.AppContainers;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// What runs in the Container Apps environment: the edge, every service, Keycloak and the broker, each a Container App with
/// one replica always running; only the edge is reachable from outside (the auth proxy joins it in PR-C), and the Aspire
/// dashboard is not deployed.
/// </summary>
public class AppHostComputeTests
{
    [Fact]
    public async Task Only_the_edge_is_external()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var external = builder.Resources
            .Where(resource => resource.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.IsExternal))
            .Select(resource => resource.Name)
            .Order(StringComparer.Ordinal);

        external.ShouldBe(["host"]);
    }

    [Fact]
    public async Task The_edge_every_service_keycloak_and_the_broker_are_container_apps()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var expected = PublishedAppHost.Services(builder).Select(service => service.Name).Concat(["host", "keycloak", "messaging"]);

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
