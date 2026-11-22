using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Red-test stub for #15 (task A1): finds nothing. B2 reads the blob's properties from the module's own container, the
/// connection string <c>blobs</c>.
/// </summary>
public sealed class AzureStoredBlobs : IStoredBlobs
{
    public AzureStoredBlobs(IConfiguration configuration) => Ensure.That(configuration).IsNotNull();

    public Task<Option<StoredBlob>> FindAsync(BlobName name, CancellationToken cancellationToken) => Task.FromResult(Option<StoredBlob>.None);
}
