namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Server-side reads from the module's own container, for a blob the module streams on itself, such as the PDFs an
/// export packs (docs/adr/0002, docs/adr/0032). A blob that is not there, or any other failure, is thrown: a module reads
/// only what it knows it stored.
/// </summary>
public interface IBlobReads
{
    /// <summary>A stream over the blob's content, read as it is consumed; the caller disposes it.</summary>
    Task<Stream> OpenReadAsync(BlobName name, CancellationToken cancellationToken);
}
