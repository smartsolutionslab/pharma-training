namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>A blob as the storage service reports it: its content type as uploaded, its length in bytes and whether it is a block blob.</summary>
public sealed record StoredBlob(BlobName Name, string ContentType, long Length, bool IsBlockBlob);
