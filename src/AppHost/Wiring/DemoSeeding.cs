using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Run mode only: the demo seeder (#457, docs/adr/0041), a project that seeds demo data through the edge's public API
/// once the edge (which waits for every service), Keycloak and the web app are up, then exits. It is on by default and
/// off with <c>DemoSeeder:Enabled=false</c> (user secrets or <c>DemoSeeder__Enabled</c>). It administers the demo users as
/// Keycloak's own administrator, whose password is the parameter Aspire generated for Keycloak; like the realm's local
/// users, a demo user's password is their username, so it gets no password of its own. Published, it is no resource.
/// </summary>
public static class DemoSeeding
{
    private const string Name = "demo-seeder";

    private const string EnabledSetting = "DemoSeeder:Enabled";

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

    private static bool Enabled(IDistributedApplicationBuilder builder) =>
        !bool.TryParse(builder.Configuration[EnabledSetting], out var enabled) || enabled;
}
