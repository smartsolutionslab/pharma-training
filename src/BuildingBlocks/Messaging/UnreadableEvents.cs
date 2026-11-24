using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// The events one pass of an announcement sweep skipped because it could not load or map them (#360, docs/adr/0016),
/// in the order the pass read them. The pass logs them once, as one Warning naming how many it skipped and the first and
/// the last of them by stream and version, and the type of the first failure, instead of one Warning per event: a pass
/// reads up to <see cref="AnnouncementSweepSettings.MostEventsPerPass"/> events, on every replica, every interval, for as
/// long as such events stay in the window. The type alone, never the exception: a message about the stored data may
/// quote it, and it is about a learner.
/// </summary>
public sealed partial class UnreadableEvents(string eventType)
{
    private UnannouncedEvent? first;
    private UnannouncedEvent? last;
    private string? firstFailureType;

    /// <summary>How many events the pass skipped so far.</summary>
    public int Count { get; private set; }

    /// <summary>Notes an event the pass skipped, with the failure it skipped it for.</summary>
    public void Add(UnannouncedEvent skipped, Exception failure)
    {
        Ensure.That(skipped).IsNotNull();
        Ensure.That(failure).IsNotNull();

        if (Count == 0)
        {
            first = skipped;
            firstFailureType = failure.GetType().Name;
        }

        last = skipped;
        Count++;
    }

    /// <summary>Logs the one Warning of the pass, or nothing when it skipped no event.</summary>
    public void LogSummary(ILogger logger)
    {
        Ensure.That(logger).IsNotNull();

        if (first is null || last is null || firstFailureType is null)
        {
            return;
        }

        LogSkipped(logger, Count, eventType, first.StreamId, first.StreamVersion, last.StreamId, last.StreamVersion, firstFailureType);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Count} {EventType} events that could not be read or mapped, the first of stream {FirstStreamId} at version {FirstStreamVersion}, the last of stream {LastStreamId} at version {LastStreamVersion}; the first failed with {FirstFailureType}. The next pass tries them again.")]
    private static partial void LogSkipped(ILogger logger, int count, string eventType, Guid firstStreamId, long firstStreamVersion, Guid lastStreamId, long lastStreamVersion, string firstFailureType);
}
