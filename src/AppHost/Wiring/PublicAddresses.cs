using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;

namespace Aspire.Hosting;

/// <summary>
/// The public address of a published app with external ingress, <c>https://&lt;app&gt;.&lt;the environment's default
/// domain&gt;</c>, where Container Apps serves it (X5 on #114, docs/adr/0005). It is built from the environment's own
/// output, never from an endpoint reference between apps: Keycloak names the auth proxy's address and the edge's, and the
/// proxy and the edge reach Keycloak, which as references would make a cycle. Publish mode only.
/// </summary>
public static class PublicAddresses
{
    // The environment's output Aspire writes in its Bicep; the property that reads it is not public.
    private const string DefaultDomainOutput = "AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN";

    /// <summary>The public address of <paramref name="app"/>, scheme and host, without a trailing slash.</summary>
    public static ReferenceExpression Of<T>(IResourceBuilder<T> app)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(app);

        var expression = new ReferenceExpressionBuilder();
        expression.AppendLiteral($"https://{app.Resource.Name}.");
        expression.AppendValueProvider(DefaultDomain(app.ApplicationBuilder));
        return expression.Build();
    }

    /// <summary>The environment's default domain, as the output of its Bicep.</summary>
    public static BicepOutputReference DefaultDomain(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var environment = builder.Resources.OfType<AzureContainerAppEnvironmentResource>().SingleOrDefault()
            ?? throw new InvalidOperationException("Nothing is published to a Container Apps environment; add it first (AddContainerAppsEnvironment).");
        return new BicepOutputReference(DefaultDomainOutput, environment);
    }
}
