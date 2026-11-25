using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The demo seeder (#457) is a project the AppHost starts in run mode alone, after the edge, Keycloak and the web app, with
/// target Local; on by default, off with <c>DemoSeeder:Enabled=false</c>. Published, staging and production alike, it is no
/// resource at all. A value of <c>DemoSeeder:Enabled</c> that is not a bool stops the AppHost, naming the key, rather than
/// leave the seeder on (#457 PR 2). The edge is told the environment it was published for (<c>Edge__Environment</c>), which
/// its <c>/api/config.json</c> reports and the seeder checks before it sends a credential to staging.
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
            var seeder = builder.Resources.OfType<DotnetProjectResource>().SingleOrDefault(resource => resource.Name == Seeder);
            seeder.ShouldNotBeNull($"Run mode has no {Seeder} project by default.");
            Path.GetFileName(seeder.GetProjectMetadata().ProjectPath).ShouldBe("DemoSeeder.csproj");
            // Aspire's own resources (__dotnet-project-build, the coordinated build every .NET project waits for) are left out.
            seeder.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name).Where(name => !name.StartsWith("__", StringComparison.Ordinal))
                .ShouldBe(["host", "keycloak", "web"], ignoreOrder: true, "The seeder starts once the edge (which waits for every service), Keycloak and the web app are up.");

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
            published.Resources.OfType<DotnetProjectResource>()
                .Select(project => Path.GetFileName(project.GetProjectMetadata().ProjectPath))
                .ShouldNotContain("DemoSeeder.csproj", $"Publishing {environment} contains the demo seeder's project.");
        }
    }

    [Theory]
    [InlineData("off")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("yes")]
    public async Task A_value_of_DemoSeeder_Enabled_that_is_not_a_bool_stops_the_apphost_naming_the_key(string value)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        Exception? failure = null;
        try
        {
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([$"--DemoSeeder:Enabled={value}"], timeout.Token);
            builder.Resources.Select(resource => resource.Name).ShouldNotContain(Seeder, $"DemoSeeder:Enabled={value} left the seeder on.");
        }
        catch (Exception exception) when (exception is not ShouldAssertException && !timeout.IsCancellationRequested)
        {
            failure = exception;
        }

        failure.ShouldNotBeNull($"DemoSeeder:Enabled={value} is not a bool; the AppHost should stop, naming the key, rather than run with the seeder on or off by a guess.");
        Messages(failure).ShouldContain("DemoSeeder:Enabled", Case.Sensitive, $"The AppHost stopped, but without naming the key: {Messages(failure)}");
    }

    [Theory]
    [InlineData(PublishedAppHost.Staging)]
    [InlineData(PublishedAppHost.Production)]
    public async Task Published_the_edge_is_told_the_environment_it_was_published_for(string environment)
    {
        await using var builder = await PublishedAppHost.ModelAsync(environment, TestContext.Current.CancellationToken);
        var host = builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == "host");

        var variables = await EnvironmentAsync(builder, host);

        variables.ShouldContainKey("Edge__Environment", $"Published ({environment}), the edge is not told its environment, so /api/config.json cannot report it and the demo seeder cannot tell staging from production. Its environment: {string.Join(", ", variables.Keys.Order(StringComparer.Ordinal))}");
        variables["Edge__Environment"].ShouldBe(environment, $"Published ({environment}), the edge should report the environment it was published for.");
    }

    [Fact]
    public async Task Run_mode_tells_the_edge_an_environment_that_is_neither_staging_nor_production()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ModelTimeout);
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(timeout.Token);
        var host = builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == "host");

        var variables = await EnvironmentAsync(builder, host);

        variables.ShouldContainKey("Edge__Environment", "In run mode the edge is not told its environment; /api/config.json reports it in every mode.");
        variables["Edge__Environment"].ToString().ShouldNotBeNullOrWhiteSpace();
        variables["Edge__Environment"].ToString()!.ToUpperInvariant().ShouldNotBeOneOf(["STAGING", "PRODUCTION"], "Run mode's edge must never report a deployed environment.");
    }

    private static string Messages(Exception exception)
    {
        List<string> messages = [];
        for (var each = exception; each is not null; each = each.InnerException)
        {
            messages.Add($"{each.GetType().Name}: {each.Message}");
            if (each is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => $"{inner.GetType().Name}: {inner.Message}"));
            }
        }

        return string.Join(" | ", messages);
    }

    private static async Task<Dictionary<string, object>> EnvironmentAsync(IDistributedApplicationTestingBuilder builder, DotnetProjectResource resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
