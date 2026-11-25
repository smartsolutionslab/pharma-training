namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// That Learning erased a person (#115), and when: a tombstone that holds no name, so a membership message for them that
/// arrives later is dropped. Written once, by the consumer of <c>PersonErasureRequestedV1</c>.
/// </summary>
public sealed class ErasedPersonCopy
{
    public Guid PersonId { get; set; }

    public DateTimeOffset ErasedAt { get; set; }
}
