namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Server-side writes into the module's own container, for a blob the module makes itself, such as a document it renders
/// (docs/adr/0002). A write only ever creates: a blob already under the name is never overwritten, so the first bytes
/// stored win.
/// </summary>
public interface IBlobWrites
{
    /// <summary>
    /// Creates the blob <paramref name="name"/> with <paramref name="content"/>, served as <paramref name="contentType"/>
    /// (one bare <c>type/subtype</c>), unless a blob already lies there, which is kept as it is
    /// (<see cref="BlobWriteOutcome.AlreadyThere"/>). A missing name or content, or a content type that is not one bare
    /// type, is refused with a <see cref="GuardException"/> before any request is made; any other failure is thrown.
    /// </summary>
    Task<BlobWriteOutcome> CreateAsync(BlobName name, BinaryData content, string contentType, CancellationToken cancellationToken);
}
