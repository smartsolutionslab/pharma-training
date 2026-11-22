using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Reads a blob's properties in the module's own container, the connection string <c>blobs</c> (docs/adr/0002), so the
/// module that accepts an upload can check what was uploaded. The container is read when the first blob is looked up,
/// so a host without it starts and fails only where a blob is needed.
/// </summary>
public sealed class AzureStoredBlobs : IStoredBlobs
{
    private const int NotFound = 404;

    private readonly Lazy<BlobContainerClient> container;

    public AzureStoredBlobs(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        container = new Lazy<BlobContainerClient>(() => ModuleBlobContainer.From(configuration));
    }

    public async Task<Option<StoredBlob>> FindAsync(BlobName name, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();

        try
        {
            var properties = (await container.Value.GetBlobClient(name.Value).GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
            return Option<StoredBlob>.Some(new StoredBlob(name, properties.ContentType ?? string.Empty, properties.ContentLength, properties.BlobType == BlobType.Block));
        }
        catch (RequestFailedException failed) when (failed.Status == NotFound)
        {
            return Option<StoredBlob>.None;
        }
    }
}
