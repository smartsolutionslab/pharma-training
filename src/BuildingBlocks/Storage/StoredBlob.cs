namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// A blob as the storage service reports it: its content type as uploaded, its length in bytes, whether it is a block
/// blob, its ETag (the quoted header value, which names this version of the blob), whether a server-side copy created it
/// rather than a direct upload, and the content disposition and encoding its uploader set, if any.
/// </summary>
public sealed record StoredBlob(
    BlobName Name,
    string ContentType,
    long Length,
    bool IsBlockBlob,
    string ETag,
    bool IsCopy = false,
    string? ContentDisposition = null,
    string? ContentEncoding = null);
