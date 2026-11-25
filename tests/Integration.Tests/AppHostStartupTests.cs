using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// How fast and how reproducibly the application starts in run mode, read from the AppHost's model, which is built and
/// never started. The containers outlive the AppHost, so a restart finds them running and healthy; a service starts
/// without waiting for Keycloak, whose metadata it reads on its first request; and the broker runs one version on every
/// machine, by digest.
/// </summary>
public class AppHostStartupTests
{
    private const string Keycloak = "keycloak";

    // RabbitMQ 4.3.8's multi-platform index digests: with the management plugin in run mode, without it published.
    private const string RunModeBrokerDigest = "8379d558577b24d4a4e543982102fd72707424862b3e0bb878fd3a90c346dc0e";

    private const string PublishedBrokerDigest = "5229e50888ca62aefe469dca4fbab87e3fa7289089b0a60f3eaf2cdb4e0ac4d1";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Run_mode_keeps_every_container_running_between_runs()
    {
        await using var builder = await RunModeAsync();

        var containers = builder.Resources.Where(resource => resource.Annotations.OfType<ContainerImageAnnotation>().Any()).ToList();

        containers.ShouldNotBeEmpty();
        containers
            .Where(container => container.Annotations.OfType<ContainerLifetimeAnnotation>().LastOrDefault()?.Lifetime != ContainerLifetime.Persistent)
            .Select(container => container.Name)
            .ShouldBeEmpty("These containers are created again at every start; run mode keeps them with ContainerLifetime.Persistent.");
    }

    [Fact]
    public async Task No_service_waits_for_Keycloak_but_the_edge_does()
    {
        await using var builder = await RunModeAsync();

        // The service hosts, found as the AppHost finds them (src/Services/<Module>/Host/Host.csproj).
        var services = builder.Resources.OfType<DotnetProjectResource>()
            .Where(project => IsServiceHost(project.GetProjectMetadata().ProjectPath))
            .ToList();

        services.ShouldNotBeEmpty();
        services
            .Where(service => service.Annotations.OfType<WaitAnnotation>().Any(wait => wait.Resource.Name == Keycloak))
            .Select(service => service.Name)
            .ShouldBeEmpty("A service reads Keycloak's metadata on its first request; waiting for Keycloak delays its start by Keycloak's.");
        var edge = builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == "host");
        edge.Annotations.OfType<WaitAnnotation>().ShouldContain(
            wait => wait.Resource.Name == Keycloak,
            "The edge is healthy only once a user can sign in, which the integration tests and the web app wait for.");
    }

    [Fact]
    public async Task Run_mode_runs_RabbitMQ_4_3_8_with_its_management_plugin_by_digest()
    {
        await using var builder = await RunModeAsync();

        var image = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem()
            .Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();

        image.Image.ShouldBe("library/rabbitmq");
        image.SHA256.ShouldBe(RunModeBrokerDigest);
    }

    [Fact]
    public async Task Published_the_broker_runs_RabbitMQ_4_3_8_without_its_management_plugin_by_digest()
    {
        await using var builder = await PublishedAppHost.ModelAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);

        var image = builder.Resources.OfType<RabbitMQServerResource>().ShouldHaveSingleItem()
            .Annotations.OfType<ContainerImageAnnotation>().ShouldHaveSingleItem();

        image.Image.ShouldBe("library/rabbitmq");
        image.SHA256.ShouldBe(PublishedBrokerDigest);
    }

    private static bool IsServiceHost(string projectPath)
    {
        var host = Path.GetDirectoryName(projectPath);
        var services = Path.GetDirectoryName(Path.GetDirectoryName(host));
        return Path.GetFileName(projectPath) == "Host.csproj"
            && Path.GetFileName(host) == "Host"
            && Path.GetFileName(services) == "Services";
    }

    private static async Task<IDistributedApplicationTestingBuilder> RunModeAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        return await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
    }
}
