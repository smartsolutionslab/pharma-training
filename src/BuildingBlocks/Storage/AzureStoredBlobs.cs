using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Reads a blob's properties and leading bytes in the module's own container, the connection string <c>blobs</c>
/// (docs/adr/0002), so the module that accepts an upload can check what was uploaded and how: put directly, or created
/// by a server-side copy, and whether its bytes are what its declared content type claims. The container is read when
/// the first blob is looked up, so a host without it starts and fails only where a blob is needed. Only a missing blob
/// is read as absent; any other failure, a missing container first, is the storage service's and is thrown.
/// </summary>
public sealed class AzureStoredBlobs : IStoredBlobs
{
    private const int RangeNotSatisfiable = 416;

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
            // A copy is told by its source or id, not its status: a blob put directly reports a status too (Azurite: Pending).
            var isCopy = properties.CopySource is not null || !string.IsNullOrEmpty(properties.CopyId);
            return Option<StoredBlob>.Some(new StoredBlob(name, properties.ContentType ?? string.Empty, properties.ContentLength, properties.BlobType == BlobType.Block, isCopy));
        }
        catch (RequestFailedException failed) when (failed.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return Option<StoredBlob>.None;
        }
    }

    /// <summary>
    /// A ranged download of the blob's first <paramref name="count"/> bytes. A shorter blob answers what it has; an empty
    /// one, whose range the service cannot satisfy, and a missing one answer no bytes.
    /// </summary>
    public async Task<ReadOnlyMemory<byte>> ReadStartAsync(BlobName name, int count, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();
        Ensure.That(count).AtLeast(1);

        try
        {
            var options = new BlobDownloadOptions { Range = new HttpRange(0, count) };
            var downloaded = (await container.Value.GetBlobClient(name.Value).DownloadContentAsync(options, cancellationToken)).Value;
            return downloaded.Content.ToMemory();
        }
        catch (RequestFailedException failed) when (failed.ErrorCode == BlobErrorCode.BlobNotFound || failed.Status == RangeNotSatisfiable)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}
