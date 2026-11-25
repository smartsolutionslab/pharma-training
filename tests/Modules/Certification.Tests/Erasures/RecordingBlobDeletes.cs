using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>The blobs deleted, in order, each also written down in the test's journal as <c>delete:{name}</c>.</summary>
internal sealed class RecordingBlobDeletes(List<string> journal) : IBlobDeletes
{
    private readonly List<string> journal = journal;

    public List<BlobName> Deleted { get; } = [];

    public Task DeleteAsync(BlobName name, CancellationToken cancellationToken)
    {
        Deleted.Add(name);
        journal.Add($"delete:{name.Value}");
        return Task.CompletedTask;
    }
}
