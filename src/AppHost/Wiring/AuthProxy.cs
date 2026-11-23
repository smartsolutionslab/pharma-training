using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Yarp;

namespace Aspire.Hosting;

/// <summary>
/// Published, the browser's one way to Keycloak (OQ2 and X2 on #114, docs/adr/0005): a YARP app, <c>auth</c>, with external
/// ingress like the edge, forwarding the realm <c>pharma-training</c> and Keycloak's static resources to Keycloak's
/// <c>http</c> endpoint, which stays internal, and nothing else. Not <c>/realms/master</c>, whose token endpoint takes the
/// administrator's password, not <c>/admin</c>, not the management port: those answer 404 from YARP, and the administrator
/// works with <c>kcadm.sh</c> inside Keycloak's container. It reads the scheme and host the Container Apps ingress forwards,
/// and passes them on. Its address, <c>https://auth.&lt;the environment's default domain&gt;</c>, is Keycloak's hostname
/// and the issuer the edge and every service validate (<see cref="Authority"/>). Run mode has none: the browser reaches
/// Keycloak's own endpoint.
/// </summary>
public static class AuthProxy
{
    private const string Name = "auth";

    private const string Realm = "pharma-training";

    // The paths the browser needs: the realm's endpoints and login pages, and the themes' static files.
    private static readonly string[] Paths = [$"/realms/{Realm}/{{**catch-all}}", "/resources/{**catch-all}"];

    /// <summary>Published, the auth proxy in front of <paramref name="keycloak"/>, which it names as its hostname; null in run mode.</summary>
    public static IResourceBuilder<YarpResource>? AddAuthProxy(this IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> keycloak)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(keycloak);

        if (!builder.ExecutionContext.IsPublishMode)
        {
            return null;
        }

        var endpoint = keycloak.GetEndpoint("http");
        var auth = builder.AddYarp(Name)
            .WithExternalHttpEndpoints()
            .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
            .WithConfiguration(yarp =>
            {
                // Keycloak's endpoint as its address, which the publisher resolves to Keycloak's internal URL; an
                // EndpointReference alone would become a service-discovery name.
                var cluster = yarp.AddCluster(keycloak.Resource.Name, ReferenceExpression.Create($"{endpoint}"));
                foreach (var path in Paths)
                {
                    yarp.AddRoute(path, cluster);
                }
            })
            .PublishAsAzureContainerApp((_, app) => app.Template.Scale.MinReplicas = 1);
        keycloak.WithEnvironment("KC_HOSTNAME", PublicAddresses.Of(auth));
        return auth;
    }

    /// <summary>
    /// The issuer the edge and every service validate tokens from: published, the auth proxy's public address; in run mode
    /// Keycloak's own endpoint.
    /// </summary>
    public static ReferenceExpression Authority(IResourceBuilder<ContainerResource> keycloak, IResourceBuilder<YarpResource>? auth)
    {
        ArgumentNullException.ThrowIfNull(keycloak);

        if (auth is null)
        {
            return ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/pharma-training");
        }

        var expression = new ReferenceExpressionBuilder();
        expression.AppendValueProvider(PublicAddresses.Of(auth));
        expression.AppendLiteral($"/realms/{Realm}");
        return expression.Build();
    }
}
