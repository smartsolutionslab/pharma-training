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
    /// accepts the upload, not by the URL (docs/adr/0002). The first PUT to a name wins, so give each upload a name the
    /// module generates (a Guid v7 segment), never one taken from user input such as the file name.
    /// </summary>
    Task<BlobUrl> CreateUploadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// A URL that reads <paramref name="name"/> (GET) and can do nothing else. It is signed with the response headers in
    /// <paramref name="headers"/> (<c>rsct</c>, <c>rscd</c>), so the storage service serves the blob with the type and
    /// disposition the module chose, whatever its uploader set, and a URL whose overrides were changed is refused.
    /// </summary>
    Task<BlobUrl> CreateReadUrlAsync(BlobName name, BlobUrlLifetime lifetime, BlobReadHeaders headers, CancellationToken cancellationToken);
}
