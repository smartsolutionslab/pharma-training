namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>
/// A stored event Assessment announced (#277, docs/adr/0016): the marker is written in the same outbox commit as the
/// announcement, so it exists exactly when the announcement left. The sweeps announce every stored ExaminationPassed,
/// failed AttemptSubmitted and ExaminationExhausted without one (#307); a passed AttemptSubmitted, announced by its
/// ExaminationPassed, is marked by its submit, or by the sweep of failures alone. Keyed by the stream and Marten's
/// version of the event in it, counted from 1.
/// </summary>
public sealed class AnnouncedEvent
{
    public Guid StreamId { get; set; }

    public long StreamVersion { get; set; }

    public DateTimeOffset AnnouncedAt { get; set; }
}
