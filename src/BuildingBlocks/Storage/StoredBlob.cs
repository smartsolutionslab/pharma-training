namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// A blob as the storage service reports it: its content type as uploaded, its length in bytes, whether it is a block
/// blob and whether a server-side copy created it rather than a direct upload.
/// </summary>
public sealed record StoredBlob(BlobName Name, string ContentType, long Length, bool IsBlockBlob, bool IsCopy = false);
