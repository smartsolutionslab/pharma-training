namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Short-lived signed URLs for blobs in the module's own container, which a browser uses directly: one to create a
/// blob, one to read it. The service signs for its own container only, so there is no container to name
/// (docs/adr/0002).
/// </summary>
public interface IBlobUrls
{
    /// <summary>A URL that creates <paramref name="name"/> once (PUT) and can do nothing else: not read, overwrite or delete it, nor touch another blob.</summary>
    Task<BlobUrl> CreateUploadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken);

    /// <summary>A URL that reads <paramref name="name"/> (GET) and can do nothing else.</summary>
    Task<BlobUrl> CreateReadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken);
}
