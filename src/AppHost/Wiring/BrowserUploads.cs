using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Aspire.Hosting;

/// <summary>
/// Lets the web app's own origin, alone, use the signed upload and read URLs from the browser: a CORS rule on the storage
/// emulator's blob service (docs/adr/0002). Aspire's storage integration has no CORS setting, so in run mode the rule is
/// set on the emulator as soon as it is ready, keeping its other properties, and the web app waits for that. A deployed
/// storage account gets its CORS rules where it is provisioned.
/// </summary>
public static class BrowserUploads
{
    private const string Methods = "GET,HEAD,PUT,OPTIONS";

    private const string AnyHeader = "*";

    private const int MaxAgeInSeconds = 3600;

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
            AllowedHeaders = AnyHeader,
            ExposedHeaders = AnyHeader,
            MaxAgeInSeconds = MaxAgeInSeconds,
        }));
        properties.Cors = rules;
        await service.SetPropertiesAsync(properties, cancellationToken);
    }
}
