namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

/// <summary>
/// A stored event Learning announced (#277, docs/adr/0016): the marker is written in the same outbox commit as the
/// announcement, so it exists exactly when the announcement left. The sweep announces every stored ContentCompleted
/// without one. Keyed by the stream and Marten's version of the event in it, counted from 1.
/// </summary>
public sealed class AnnouncedEvent
{
    public Guid StreamId { get; set; }

    public long StreamVersion { get; set; }

    public DateTimeOffset AnnouncedAt { get; set; }
}
