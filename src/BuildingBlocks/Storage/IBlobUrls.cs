namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Short-lived signed URLs for blobs in the module's own container, which a browser uses directly: one to create a
/// blob, one to read it. The service signs for its own container only, so there is no container to name
/// (docs/adr/0002).
/// </summary>
public interface IBlobUrls
{
    /// <summary>
    /// A URL that creates <paramref name="name"/> (PUT) and cannot read, overwrite or delete it, nor touch another blob.
    /// It carries the storage service's Create permission, which also allows what Create allows on that one blob
    /// (snapshots, staged blocks, a server-side copy into it); size and content type are checked by the module that
    /// accepts the upload, not by the URL (docs/adr/0002).
    /// </summary>
    Task<BlobUrl> CreateUploadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken);

    /// <summary>A URL that reads <paramref name="name"/> (GET) and can do nothing else.</summary>
    Task<BlobUrl> CreateReadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken);
}
