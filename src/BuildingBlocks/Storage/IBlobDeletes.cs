namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// Server-side deletes in the module's own container, for a blob the module made itself and must forget, such as the PDF
/// of an erased person's certificate (#115, docs/adr/0002).
/// </summary>
public interface IBlobDeletes
{
    /// <summary>
    /// Deletes the blob <paramref name="name"/>. A blob that is not there is success, so a retry deletes what is left; a
    /// missing name is refused with a <see cref="GuardException"/> before any request is made; any other failure is thrown.
    /// </summary>
    Task DeleteAsync(BlobName name, CancellationToken cancellationToken);
}
