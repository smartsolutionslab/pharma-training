using Aspire.Hosting.Dotnet;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Published, the edge and every service trust exactly one <c>X-Forwarded-For</c> hop, the Container Apps ingress in front
/// of each (#292, docs/adr/0031): <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</c>, ASP.NET Core's forwarded headers with a
/// forward limit of one and any proxy. Aspire 13.6.5 sets it on every project it publishes, and the AppHost names it on
/// the edge and the services itself; this pins it, so an Aspire that stopped setting it fails here, not in production.
/// </summary>
public class AppHostForwardedHeadersTests
{
    private const string Variable = "ASPNETCORE_FORWARDEDHEADERS_ENABLED";

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task The_published_edge_and_every_service_trust_one_forwarded_hop(string environment)
    {
        await using var builder = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);
        var edge = builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == "host");
        List<IResource> projects = [edge, .. PublishedAppHost.Services(builder)];

        projects.Count.ShouldBeGreaterThan(1);
        foreach (var project in projects)
        {
            var configuration = await ExecutionConfigurationBuilder.Create(project)
                .WithEnvironmentVariablesConfig()
                .BuildAsync(builder.ExecutionContext, NullLogger.Instance, TestContext.Current.CancellationToken);
            var variables = configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);

            variables.ShouldContainKey(Variable, $"{project.Name} does not trust the ingress's X-Forwarded-For hop.");
            variables[Variable].ShouldBe("true", $"{project.Name} does not trust the ingress's X-Forwarded-For hop.");
        }
    }
}
