using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// The blobs deleted, in order, each also written down in the test's journal as <c>delete:{name}</c>. A name in
/// <see cref="Failing"/> throws instead, as a blob the storage keeps refusing to delete would.
/// </summary>
internal sealed class RecordingBlobDeletes(List<string> journal) : IBlobDeletes
{
    private readonly List<string> journal = journal;

    public List<BlobName> Deleted { get; } = [];

    public HashSet<BlobName> Failing { get; } = [];

    public Task DeleteAsync(BlobName name, CancellationToken cancellationToken)
    {
        if (Failing.Contains(name))
        {
            journal.Add($"failed-delete:{name.Value}");
            throw new InvalidOperationException($"The storage refused to delete {name.Value}.");
        }

        Deleted.Add(name);
        journal.Add($"delete:{name.Value}");
        return Task.CompletedTask;
    }
}
