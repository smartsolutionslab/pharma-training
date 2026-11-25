using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The demo seeder (#457) is a project the AppHost starts in run mode alone, after the edge, Keycloak and the web app, with
/// target Local; on by default, off with <c>DemoSeeder:Enabled=false</c>. Published, staging and production alike, it is no
/// resource at all.
/// </summary>
public class AppHostDemoSeederTests
{
    private const string Seeder = "demo-seeder";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Run_mode_starts_the_seeder_after_the_edge_keycloak_and_the_web_app_unless_it_is_switched_off()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        await using (var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token))
        {
            var seeder = builder.Resources.OfType<ProjectResource>().SingleOrDefault(resource => resource.Name == Seeder);
            seeder.ShouldNotBeNull($"Run mode has no {Seeder} project by default.");
            Path.GetFileName(seeder.GetProjectMetadata().ProjectPath).ShouldBe("DemoSeeder.csproj");
            seeder.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name).ShouldBe(["host", "keycloak", "web"], ignoreOrder: true, "The seeder starts once the edge (which waits for every service), Keycloak and the web app are up.");

            var environment = await EnvironmentAsync(builder, seeder);
            environment.ShouldContainKey("DemoSeeder__Target");
            environment["DemoSeeder__Target"].ShouldBe("Local");
            foreach (var key in new[] { "DemoSeeder__EdgeUrl", "DemoSeeder__WebOrigin", "DemoSeeder__MailpitUrl", "DemoSeeder__KeycloakAdminUrl", "DemoSeeder__AdminUser", "DemoSeeder__AdminPassword" })
            {
                environment.ShouldContainKey(key, $"The seeder is not given {key}.");
            }

            environment.Keys.ShouldNotContain("DemoSeeder__UserPassword", "Locally a demo user's password is their username.");
        }

        await using var switchedOff = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(["--DemoSeeder:Enabled=false"], timeout.Token);
        switchedOff.Resources.Select(resource => resource.Name).ShouldNotContain(Seeder, "DemoSeeder:Enabled=false switches the seeder off.");
    }

    [Fact]
    public async Task The_seeder_is_a_resource_of_run_mode_alone()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        await using (var run = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token))
        {
            run.Resources.Select(resource => resource.Name).ShouldContain(Seeder, "Run mode starts the seeder.");
        }

        foreach (var environment in new[] { PublishedAppHost.Staging, PublishedAppHost.Production })
        {
            await using var published = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);
            published.Resources.Select(resource => resource.Name).ShouldNotContain(Seeder, $"Publishing {environment} contains the demo seeder.");
            published.Resources.OfType<ProjectResource>()
                .Select(project => Path.GetFileName(project.GetProjectMetadata().ProjectPath))
                .ShouldNotContain("DemoSeeder.csproj", $"Publishing {environment} contains the demo seeder's project.");
        }
    }

    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationTestingBuilder builder, ProjectResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
