using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Run mode only: the demo seeder (#457, docs/adr/0042), a project that seeds demo data through the edge's public API
/// once the edge (which waits for every service), Keycloak and the web app are up, then exits. It is on by default and
/// off with <c>DemoSeeder:Enabled=false</c> (user secrets or <c>DemoSeeder__Enabled</c>). It administers the demo users as
/// Keycloak's own administrator, whose password is the parameter Aspire generated for Keycloak; like the realm's local
/// users, a demo user's password is their username, so it gets no password of its own. Published, it is no resource; on
/// staging, .github/workflows/demo-seed-staging.yml runs it instead, after it has checked that the edge reports the
/// deployment staging (<see cref="WithDeploymentName"/>).
/// </summary>
public static class DemoSeeding
{
    private const string Name = "demo-seeder";

    private const string EnabledSetting = "DemoSeeder:Enabled";

    /// <summary>What the edge reports in run mode: neither staging nor production, so the seeder's staging run refuses it.</summary>
    private const string RunModeDeployment = "local";

    /// <summary>
    /// Tells the edge the deployment it runs in, <c>Edge__Environment</c>, which its <c>/api/config.json</c> reports:
    /// published, the environment's name (<c>aspire publish -e</c>); in run mode <c>local</c>, whatever the AppHost's own
    /// environment is called.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithDeploymentName(this IResourceBuilder<ProjectResource> edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        var context = edge.ApplicationBuilder.ExecutionContext;
        return edge.WithEnvironment("Edge__Environment", context.IsRunMode ? RunModeDeployment : edge.ApplicationBuilder.Environment.EnvironmentName);
    }

    public static IDistributedApplicationBuilder AddDemoSeeder(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> host,
        IResourceBuilder<IResourceWithEndpoints> web,
        IResourceBuilder<ContainerResource> keycloak)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(keycloak);

        if (!builder.ExecutionContext.IsRunMode || !Enabled(builder))
        {
            return builder;
        }

        var mailpit = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "mailpit");
        var administratorPassword = builder.Resources.OfType<ParameterResource>().Single(resource => resource.Name == "keycloak-password");
        builder.AddProject<Projects.DemoSeeder>(Name)
            .WithEnvironment("DemoSeeder__Target", "Local")
            .WithEnvironment("DemoSeeder__EdgeUrl", host.GetEndpoint("http"))
            .WithEnvironment("DemoSeeder__WebOrigin", web.GetEndpoint("http"))
            .WithEnvironment("DemoSeeder__MailpitUrl", builder.CreateResourceBuilder(mailpit).GetEndpoint("http"))
            .WithEnvironment("DemoSeeder__KeycloakAdminUrl", keycloak.GetEndpoint("http"))
            .WithEnvironment("DemoSeeder__AdminUser", "admin")
            .WithEnvironment("DemoSeeder__AdminPassword", builder.CreateResourceBuilder(administratorPassword))
            .WaitFor(host)
            .WaitFor(keycloak)
            .WaitFor(web);
        return builder;
    }

    /// <summary>On unless the setting says false; a value that is not a bool stops the AppHost, naming the key, rather than guess.</summary>
    private static bool Enabled(IDistributedApplicationBuilder builder)
    {
        var value = builder.Configuration[EnabledSetting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return bool.TryParse(value.Trim(), out var enabled)
            ? enabled
            : throw new InvalidOperationException($"{EnabledSetting} must be true or false; leave it unset to seed demo data in run mode.");
    }
}
