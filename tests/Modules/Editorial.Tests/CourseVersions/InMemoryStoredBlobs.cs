using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>The module's blob container in memory: what a test uploaded, and how often a handler looked.</summary>
internal sealed class InMemoryStoredBlobs : IStoredBlobs
{
    private readonly Dictionary<BlobName, StoredBlob> blobs = [];

    public int Lookups { get; private set; }

    /// <summary>Stores a blob as the browser would have uploaded it: a block blob, put directly, unless told otherwise.</summary>
    public void Upload(string name, string contentType, long length, bool isBlockBlob = true, bool isCopy = false)
    {
        var blobName = BlobName.From(name);
        blobs[blobName] = new StoredBlob(blobName, contentType, length, isBlockBlob, isCopy);
    }

    public Task<Option<StoredBlob>> FindAsync(BlobName name, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult(blobs.TryGetValue(name, out var blob) ? Option<StoredBlob>.Some(blob) : Option<StoredBlob>.None);
    }
}
