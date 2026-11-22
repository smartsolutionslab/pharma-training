namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// What the storage service holds under a name in the module's own container, so the module that accepts an upload can
/// check its size and content type before it records it (docs/adr/0002).
/// </summary>
public interface IStoredBlobs
{
    /// <summary>The blob's properties, or none when nothing was uploaded under <paramref name="name"/>.</summary>
    Task<Option<StoredBlob>> FindAsync(BlobName name, CancellationToken cancellationToken);
}
