namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// What the storage service holds under a name in the module's own container, so the module that accepts an upload can
/// check its size and content type before it records it (docs/adr/0002).
/// </summary>
public interface IStoredBlobs
{
    /// <summary>The blob's properties, or none when nothing was uploaded under <paramref name="name"/>.</summary>
    Task<Option<StoredBlob>> FindAsync(BlobName name, CancellationToken cancellationToken);

    /// <summary>
    /// The first <paramref name="count"/> bytes of the blob <see cref="FindAsync"/> found, or all of them when it is
    /// shorter, so the module can check what was uploaded rather than the content type its uploader declared. The read is
    /// of that version only: a blob changed since it was found answers no bytes.
    /// </summary>
    Task<ReadOnlyMemory<byte>> ReadStartAsync(StoredBlob blob, int count, CancellationToken cancellationToken);
}
