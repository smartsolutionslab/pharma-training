namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The marker a module writes in the outbox commit of each announcement of a stored event (#277, docs/adr/0016), in its
/// <c>announced_events</c>. Its primary key lets one of two writers that announce the same event commit; a unique
/// violation of it, and of nothing else, means another writer announced the event first.
/// </summary>
public static class AnnouncedEventMarker
{
    /// <summary>The primary key of <c>announced_events</c>, (stream_id, stream_version), in every module.</summary>
    public const string PrimaryKey = "pk_announced_events";
}
