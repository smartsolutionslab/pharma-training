using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Yarp;

namespace Aspire.Hosting;

/// <summary>
/// Published, the browser's one way to Keycloak (OQ2 and X2 on #114, docs/adr/0005): a YARP app, <c>auth</c>, with external
/// ingress like the edge, forwarding the realm <c>pharma-training</c>, its admin API's users and roles (#457) and Keycloak's static resources to
/// Keycloak's <c>http</c> endpoint, which stays internal, and nothing else. Not <c>/realms/master</c>, whose token endpoint
/// takes the administrator's password, not the rest of <c>/admin</c>, not the management port: those answer 404 from YARP, and the administrator
/// works with <c>kcadm.sh</c> inside Keycloak's container. It reads the scheme and host the Container Apps ingress forwards,
/// and passes them on. Its address, <c>https://auth.&lt;the environment's default domain&gt;</c>, is Keycloak's hostname
/// and the issuer the edge and every service validate (<see cref="Authority"/>). Run mode has none: the browser reaches
/// Keycloak's own endpoint.
/// </summary>
public static class AuthProxy
{
    private const string Name = "auth";

    /// <summary>The realm the application signs in with.</summary>
    internal const string Realm = "pharma-training";

    // YARP's image (mcr.microsoft.com/dotnet/nightly/yarp) by tag and digest (the security review of #114): Aspire's default
    // tag, 2.3-preview, moves with every nightly build. Dependabot cannot see an image named in C#, so a new one is pinned
    // here by hand, tag and digest together, from `docker buildx imagetools inspect mcr.microsoft.com/dotnet/nightly/yarp:<tag>`.
    private const string ImageTag = "2.3.0-preview.5";

    private const string ImageDigest = "540266215e741cebd0152343f8a6018ee7ebc34f7caaa4022e3a93ff2f778b58";

    // The paths the browser needs: the realm's endpoints and login pages, and the themes' static files.
    private static readonly string[] Paths = [$"/realms/{Realm}/{{**catch-all}}", "/resources/{**catch-all}"];

    // Staging alone also forwards the realm's admin users (#457): a manage-users client of the realm (the demo seeder's,
    // X1's) creates and updates its users from outside the environment, with its own token from the realm's token endpoint,
    // and Keycloak checks its realm-management roles on every call. Roles are read through the users' role mappings, so no
    // other admin path is needed. Production's public host exposes no admin API at all: a leaked manage-users secret there
    // (the erasure client's) reaches nothing from the internet. The console, other realms and /realms/master stay
    // unreachable everywhere.
    private static readonly string[] StagingAdminPaths = [$"/admin/realms/{Realm}/users/{{**catch-all}}"];

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
            .WithImageTag(ImageTag)
            .WithImageSHA256(ImageDigest)
            .WithExternalHttpEndpoints()
            .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
            .WithConfiguration(yarp =>
            {
                // Keycloak's endpoint as its address, which the publisher resolves to Keycloak's internal URL; an
                // EndpointReference alone would become a service-discovery name.
                var cluster = yarp.AddCluster(keycloak.Resource.Name, ReferenceExpression.Create($"{endpoint}"));
                var paths = builder.Environment.EnvironmentName == DatabaseServerSize.Staging ? [.. Paths, .. StagingAdminPaths] : Paths;
                foreach (var path in paths)
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
