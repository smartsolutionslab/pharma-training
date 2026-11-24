namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// A stored event with no marker in the module's <c>announced_events</c> (#277, docs/adr/0016): its announcement never
/// left. Marten's id of the event, the stream it is stored in, its version there (counted from 1) and when Marten stored
/// it, all as the event store has them; its data is loaded on its own, so one event that cannot be read holds up no other.
/// </summary>
public sealed record UnannouncedEvent(Guid EventId, Guid StreamId, long StreamVersion, DateTimeOffset StoredAt);
