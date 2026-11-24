namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// A stored event with no marker in the module's <c>announced_events</c> (#277, docs/adr/0016): its announcement never
/// left. Marten's id of the event, the stream it is stored in, its version there (counted from 1), when Marten stored
/// it and its place in Marten's sequence of all the module's events (<c>seq_id</c>, counted from 1), all as the event
/// store has them; its data is loaded on its own, so one event that cannot be read holds up no other. A pass reads the
/// next page after the <see cref="Sequence"/> of the last event it read, so it reads on past events it skipped.
/// </summary>
public sealed record UnannouncedEvent(Guid EventId, Guid StreamId, long StreamVersion, DateTimeOffset StoredAt, long Sequence)
{
    /// <summary>The cursor before the first event: Marten's sequence starts at 1.</summary>
    public const long BeforeFirst = 0;
}
