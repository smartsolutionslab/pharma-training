using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// The module's container in memory, read as AzureBlobReads reads it: a stream over the blob's bytes, and a failure for a
/// blob that is not there. Keeps every read it was asked for, in order.
/// </summary>
internal sealed class InMemoryBlobReads : IBlobReads
{
    public Dictionary<BlobName, byte[]> Blobs { get; } = [];

    public List<BlobName> Reads { get; } = [];

    public Task<Stream> OpenReadAsync(BlobName name, CancellationToken cancellationToken)
    {
        Reads.Add(name);
        if (!Blobs.TryGetValue(name, out var content))
        {
            throw new InvalidOperationException($"No blob lies at {name}.");
        }

        return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
    }
}
