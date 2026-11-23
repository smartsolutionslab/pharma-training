using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Aspire.Hosting;

/// <summary>
/// Lets the web app's own origin, alone, use the signed upload and read URLs from the browser: a CORS rule on the storage
/// emulator's blob service (docs/adr/0002). Aspire's storage integration has no CORS setting, so in run mode the rule is
/// set on the emulator as soon as it is ready, keeping its other properties, and the web app waits for that. Published,
/// the web app is served by the edge and reached at its public address, the parameter <c>frontend-url</c>, so the storage
/// account's blob service has one rule, the same, for those two origins alone, written into its Bicep. No wildcard.
/// </summary>
public static class BrowserUploads
{
    private const string Methods = "GET,HEAD,PUT,OPTIONS";

    // What a browser upload or read sends and needs to see, and no more: no x-ms-copy-source, no page-blob headers.
    private const string AllowedHeaders = "content-type,x-ms-blob-type,x-ms-blob-content-type,x-ms-version,x-ms-date,x-ms-client-request-id";

    private const string ExposedHeaders = "etag,content-type,content-length,x-ms-request-id";

    private const int MaxAgeInSeconds = 3600;

    private const string DefaultDomain = "environment_default_domain";

    // The storage module's parameter main.bicep sets to frontend-url.
    private const string FrontendUrl = "frontend_url";

    // The environment's output Aspire writes in its Bicep; the property that reads it is not public.
    private const string DefaultDomainOutput = "AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN";

    public static IResourceBuilder<AzureStorageResource> AllowBrowserAccessFrom<TWeb>(this IResourceBuilder<AzureStorageResource> storage, IResourceBuilder<TWeb> web)
        where TWeb : IResourceWithEndpoints, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(web);

        if (!storage.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return storage;
        }

        var endpoint = web.GetEndpoint("http");
        // Anything waiting for storage (the web app, a test waiting for it to be healthy) goes on only once the ready
        // event's subscribers, this one among them, have finished.
        storage.ApplicationBuilder.Eventing.Subscribe<ResourceReadyEvent>(storage.Resource, async (_, cancellationToken) =>
            await AllowAsync(
                await BlobService(storage).ConnectionStringExpression.GetValueAsync(cancellationToken)
                    ?? throw new InvalidOperationException($"{storage.Resource.Name} has no blob connection string."),
                await OriginsAsync(endpoint, cancellationToken),
                cancellationToken));
        web.WaitFor(storage);
        return storage;
    }

    /// <summary>
    /// Published, the edge's origin, <c>https://&lt;edge&gt;.&lt;the environment's default domain&gt;</c>, where a
    /// Container App with external ingress is served, and the web app's public address, <paramref name="frontendUrl"/>,
    /// which the deployment sets as an origin (scheme and host, no path or trailing slash), those two alone; in run mode
    /// nothing (<see cref="AllowBrowserAccessFrom"/> does it).
    /// </summary>
    public static IResourceBuilder<AzureStorageResource> AllowBrowserAccessFromEdge(this IResourceBuilder<AzureStorageResource> storage, IResourceBuilder<ProjectResource> edge, IResourceBuilder<ParameterResource> frontendUrl)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(frontendUrl);

        if (!storage.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return storage;
        }

        var environment = storage.ApplicationBuilder.Resources.OfType<AzureContainerAppEnvironmentResource>().SingleOrDefault()
            ?? throw new InvalidOperationException($"{edge.Resource.Name} is published to no Container Apps environment; add it first (AddContainerAppsEnvironment).");
        var edgeName = edge.Resource.Name;
        return storage
            .WithParameter(DefaultDomain, new BicepOutputReference(DefaultDomainOutput, environment))
            .WithParameter(FrontendUrl, frontendUrl)
            .ConfigureInfrastructure(infrastructure =>
            {
                var domain = new ProvisioningParameter(DefaultDomain, typeof(string));
                infrastructure.Add(domain);
                var frontend = new ProvisioningParameter(FrontendUrl, typeof(string));
                infrastructure.Add(frontend);
                var rule = new StorageCorsRule { MaxAgeInSeconds = MaxAgeInSeconds };
                rule.AllowedOrigins.Add(new InterpolatedStringExpression([new StringLiteralExpression($"https://{edgeName}."), new IdentifierExpression(domain.BicepIdentifier)]));
                rule.AllowedOrigins.Add(frontend);
                foreach (var method in Methods.Split(','))
                {
                    rule.AllowedMethods.Add(Enum.Parse<CorsRuleAllowedMethod>(method, ignoreCase: true));
                }

                foreach (var header in AllowedHeaders.Split(','))
                {
                    rule.AllowedHeaders.Add(header);
                }

                foreach (var header in ExposedHeaders.Split(','))
                {
                    rule.ExposedHeaders.Add(header);
                }

                infrastructure.GetProvisionableResources().OfType<BlobService>().Single().CorsRules.Add(rule);
            });
    }

    /// <summary>The account's blob service, which the module's containers (AddBlobContainer) are children of.</summary>
    private static AzureBlobStorageResource BlobService(IResourceBuilder<AzureStorageResource> storage) =>
        storage.ApplicationBuilder.Resources.OfType<AzureBlobStorageResource>().SingleOrDefault(blobs => blobs.Parent == storage.Resource)
            ?? throw new InvalidOperationException($"{storage.Resource.Name} has no blob service; add a blob container to it first.");

    /// <summary>
    /// The web app's origin as the AppHost declares it (http://localhost:5173, which the realm's redirect URIs name too)
    /// and, where it differs, as it was allocated: a test run that randomizes ports serves the app on another.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>> OriginsAsync(EndpointReference endpoint, CancellationToken cancellationToken)
    {
        HashSet<string> origins = [with(StringComparer.OrdinalIgnoreCase)];
        if (endpoint.EndpointAnnotation.Port is { } declared)
        {
            origins.Add($"{endpoint.EndpointAnnotation.UriScheme}://localhost:{declared}");
        }

        if (await endpoint.GetValueAsync(cancellationToken) is { Length: > 0 } allocated)
        {
            origins.Add(new Uri(allocated).GetLeftPart(UriPartial.Authority));
        }

        return origins.Count > 0
            ? origins
            : throw new InvalidOperationException($"{endpoint.Resource.Name} has no http endpoint to allow.");
    }

    private static async Task AllowAsync(string connectionString, IReadOnlyCollection<string> origins, CancellationToken cancellationToken)
    {
        var service = new BlobServiceClient(connectionString);
        var properties = (await service.GetPropertiesAsync(cancellationToken)).Value;
        // One rule per origin, whatever an earlier run (on a kept data volume) left for it; other rules stay as they are.
        List<BlobCorsRule> rules = [.. properties.Cors.Where(rule => !origins.Contains(rule.AllowedOrigins, StringComparer.OrdinalIgnoreCase))];
        rules.AddRange(origins.Select(origin => new BlobCorsRule
        {
            AllowedOrigins = origin,
            AllowedMethods = Methods,
            AllowedHeaders = AllowedHeaders,
            ExposedHeaders = ExposedHeaders,
            MaxAgeInSeconds = MaxAgeInSeconds,
        }));
        properties.Cors = rules;
        await service.SetPropertiesAsync(properties, cancellationToken);
    }
}
