using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Deletes a blob in the module's own container, the connection string <c>blobs</c> (docs/adr/0002), with Delete Blob
/// (#115). The storage service's 404 <c>BlobNotFound</c> is success, so a retry deletes what is left; any other failure
/// (a missing container, a refused identity, a lease) is thrown. It is not <c>DeleteIfExists</c>, which would take a
/// missing container for a deleted blob too. The container is read when the first blob is deleted, so a host without it
/// starts and fails only where a delete is needed.
/// </summary>
public sealed class AzureBlobDeletes : IBlobDeletes
{
    private const int NotFound = 404;

    private readonly Lazy<BlobContainerClient> container;

    public AzureBlobDeletes(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        container = new Lazy<BlobContainerClient>(() => ModuleBlobContainer.From(configuration));
    }

    public async Task DeleteAsync(BlobName name, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();

        try
        {
            await container.Value.GetBlobClient(name.Value).DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots, conditions: null, cancellationToken);
        }
        catch (RequestFailedException failed) when (failed.Status == NotFound && failed.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            // Already gone: an earlier attempt deleted it, or it was never written. The retry's goal is reached.
        }
    }
}
