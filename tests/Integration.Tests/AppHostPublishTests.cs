using Aspire.Hosting.Azure.AppContainers;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The AppHost publishes one Azure Container Apps environment, <c>aca</c>, for each environment the deploy workflow names,
/// and writes the same Bicep for the same commit every time (docs/adr/0004).
/// </summary>
public class AppHostPublishTests
{
    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_published_model_has_one_container_app_environment(string environment)
    {
        await using var builder = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);

        var containerApps = builder.Resources.OfType<AzureContainerAppEnvironmentResource>().ShouldHaveSingleItem();

        containerApps.Name.ShouldBe("aca");
    }

    [Fact]
    public async Task The_run_model_has_no_container_app_environment()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(TestContext.Current.CancellationToken);

        builder.Resources.OfType<AzureContainerAppEnvironmentResource>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Publishing_writes_the_container_app_environment_into_main_bicep(string environment)
    {
        var bicep = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        bicep.Files.ShouldContainKey("main.bicep");
        bicep.Module("aca").ShouldContain("'Microsoft.App/managedEnvironments@");
        bicep.Files["main.bicep"].ShouldContain("module aca 'aca/aca.bicep'");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Publishing_twice_writes_the_same_bicep(string environment)
    {
        var first = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);
        var second = await PublishedAppHost.PublishAsync(environment, TestContext.Current.CancellationToken);

        first.ContainerApps.ShouldNotBeEmpty();
        second.Files.Keys.Order(StringComparer.Ordinal).ShouldBe(first.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, content) in first.Files)
        {
            second.Files[path].ShouldBe(content, $"{path} differs between two publishes of the same commit.");
        }
    }
}
