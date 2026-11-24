using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Documents;

/// <summary>
/// The module's container in memory, written create-only as AzureBlobWrites writes it: the first bytes under a name stay,
/// and a later write to it is <see cref="BlobWriteOutcome.AlreadyThere"/>. Keeps every write it was asked for.
/// </summary>
internal sealed class InMemoryBlobWrites : IBlobWrites
{
    public Dictionary<BlobName, (byte[] Content, string ContentType)> Blobs { get; } = [];

    public List<(BlobName Name, byte[] Content, string ContentType)> Writes { get; } = [];

    public Task<BlobWriteOutcome> CreateAsync(BlobName name, BinaryData content, string contentType, CancellationToken cancellationToken)
    {
        var bytes = content.ToArray();
        Writes.Add((name, bytes, contentType));
        if (Blobs.ContainsKey(name))
        {
            return Task.FromResult(BlobWriteOutcome.AlreadyThere);
        }

        Blobs[name] = (bytes, contentType);
        return Task.FromResult(BlobWriteOutcome.Created);
    }
}
