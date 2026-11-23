namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>
/// A <c>ContentCompletedV1</c> Assessment handled, by its event identifier: a redelivery, or a replay of the
/// dead-lettered event, finds it and changes nothing (docs/adr/0012).
/// </summary>
public sealed class HandledContentCompletion
{
    public Guid EventIdentifier { get; set; }

    public Guid SessionId { get; set; }

    public DateTimeOffset HandledAt { get; set; }
}
