using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Reads a blob from the module's own container, the connection string <c>blobs</c> (docs/adr/0002), as a stream that
/// downloads as it is read. A missing blob is the storage service's 404 and thrown like any other failure. The container
/// is read when the first blob is, so a host without it starts and fails only where a read is needed.
/// </summary>
public sealed class AzureBlobReads : IBlobReads
{
    private readonly Lazy<BlobContainerClient> container;

    public AzureBlobReads(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        container = new Lazy<BlobContainerClient>(() => ModuleBlobContainer.From(configuration));
    }

    public async Task<Stream> OpenReadAsync(BlobName name, CancellationToken cancellationToken)
    {
        Ensure.That(name).IsNotNull();

        return await container.Value.GetBlobClient(name.Value).OpenReadAsync(cancellationToken: cancellationToken);
    }
}
