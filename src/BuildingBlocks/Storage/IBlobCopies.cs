namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Server-side copies into the module's own container from a read URL on the same storage account, such as one another
/// module signed for its own blob (docs/adr/0002). The bytes never pass through the service host.
/// </summary>
public interface IBlobCopies
{
    /// <summary>
    /// Copies the blob <paramref name="source"/> reads into <paramref name="target"/>, unless a blob already lies there,
    /// which counts as copied (<see cref="BlobCopyOutcome.AlreadyThere"/>). A source outside the module's storage account
    /// (another scheme, host or port) is refused with a <see cref="GuardException"/> before any request is made; an
    /// expired or refused source fails at the storage service and is thrown.
    /// </summary>
    Task<BlobCopyOutcome> CopyFromAsync(BlobUrl source, BlobName target, CancellationToken cancellationToken);
}
