using System.Data.Common;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// One pass of a module's announcement sweep over one type of its stored events (#277, #307, docs/adr/0016). Every stored
/// event of the type without a marker, stored between the lookback and the grace period ago, is announced as the handler
/// that stored it would have announced it, oldest first, each with its marker, keyed on the stream the event is stored in,
/// in a commit of its own, and logged as a Warning naming the type, the stream and the version. An event whose
/// announcement belongs to another stored event (Assessment's passed AttemptSubmitted, announced by its ExaminationPassed)
/// is settled with its marker alone, in a commit of its own, and nothing is announced or logged for it. One that cannot be
/// read or mapped is skipped and keeps no marker; the pass reads on past it a page at a time
/// (<see cref="UnannouncedEventPages"/>, #337, #351) until it has settled a batch, reached the end of the window or read
/// as far as a pass may, and then logs one Warning for all it skipped (<see cref="UnreadableEvents"/>, #360). A database
/// failure is no unreadable data: it ends the pass. Of two writers that announce the same event (a replica's sweep, the
/// late handler), the marker's primary key lets one commit through; the other throws, and the hosted service logs it.
/// </summary>
public sealed partial class AnnouncementSweepPass
{
    private readonly string eventType;
    private readonly IUnitOfWork unitOfWork;
    private readonly Action<UnannouncedEvent, DateTimeOffset> mark;
    private readonly AnnouncementSweepSettings settings;
    private readonly TimeProvider time;
    private readonly ILogger logger;

    /// <param name="eventType">The swept event's type, as the logs name it.</param>
    /// <param name="unitOfWork">The module's unit of work, which commits each announcement with its marker.</param>
    /// <param name="mark">Adds the module's marker for the stored event, announced at the given instant, to the unit of work.</param>
    /// <param name="settings">The module's sweep settings.</param>
    /// <param name="time">The clock the window and the markers are read from.</param>
    /// <param name="logger">The logger of the module's handler.</param>
    public AnnouncementSweepPass(
        string eventType,
        IUnitOfWork unitOfWork,
        Action<UnannouncedEvent, DateTimeOffset> mark,
        AnnouncementSweepSettings settings,
        TimeProvider time,
        ILogger logger)
    {
        Ensure.That(eventType).IsNotNullOrWhiteSpace();
        Ensure.That(unitOfWork).IsNotNull();
        Ensure.That(mark).IsNotNull();
        Ensure.That(settings).IsNotNull();
        Ensure.That(time).IsNotNull();
        Ensure.That(logger).IsNotNull();

        this.eventType = eventType;
        this.unitOfWork = unitOfWork;
        this.mark = mark;
        this.settings = settings;
        this.time = time;
        this.logger = logger;
    }

    /// <param name="find">Answers at most <c>limit</c> unannounced events stored from <c>storedFrom</c> to <c>storedUntil</c>, after the sequence <c>after</c>, oldest first.</param>
    /// <param name="announcementOf">
    /// The announcement of a found event, or none when the event has none of its own and needs only its marker; throws
    /// when the event cannot be read or mapped, and with a <see cref="DbException"/> when the database fails.
    /// </param>
    /// <param name="cancellationToken">Ends the pass.</param>
    public async Task RunAsync(
        Func<DateTimeOffset, DateTimeOffset, long, int, CancellationToken, Task<IReadOnlyList<UnannouncedEvent>>> find,
        Func<UnannouncedEvent, CancellationToken, Task<Option<IntegrationEvent>>> announcementOf,
        CancellationToken cancellationToken)
    {
        Ensure.That(find).IsNotNull();
        Ensure.That(announcementOf).IsNotNull();

        var now = time.GetUtcNow();
        var storedFrom = now - settings.Lookback;
        var storedUntil = now - settings.GracePeriod;
        var pages = UnannouncedEventPages.ReadAsync(
            (after, limit, token) => find(storedFrom, storedUntil, after, limit, token),
            settings.BatchSize,
            cancellationToken);
        var settled = 0;
        var unreadable = new UnreadableEvents(eventType);
        try
        {
            await foreach (var stored in pages)
            {
                if (await AnnouncementOfAsync(stored, announcementOf, unreadable, cancellationToken) is not { } announcement)
                {
                    continue;
                }

                if (announcement.HasValue)
                {
                    await unitOfWork.PublishAsync(announcement.Value);
                }

                mark(stored, now);
                if (announcement.HasValue)
                {
                    LogReannounced(logger, eventType, stored.StreamId, stored.StreamVersion, stored.StoredAt);
                }

                await unitOfWork.CommitAsync(cancellationToken);
                settled++;
                if (settled == settings.BatchSize)
                {
                    break;
                }
            }
        }
        finally
        {
            // One Warning for every event the pass skipped (#360), also when a failure ends the pass after it skipped some.
            unreadable.LogSummary(logger);
        }
    }

    /// <summary>
    /// The announcement of a stored event, none when it needs its marker alone, or null when it cannot be read or mapped:
    /// noted for the pass's one Warning and skipped, and the pass reads on past it. It keeps no marker, so every pass tries
    /// it again until the lookback. A database failure is no unreadable data: it ends the pass.
    /// </summary>
    private static async Task<Option<IntegrationEvent>?> AnnouncementOfAsync(
        UnannouncedEvent stored,
        Func<UnannouncedEvent, CancellationToken, Task<Option<IntegrationEvent>>> announcementOf,
        UnreadableEvents unreadable,
        CancellationToken cancellationToken)
    {
        try
        {
            return await announcementOf(stored, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or DbException))
        {
            // Noted by its type alone: a message about the stored data may quote it, and it is about a learner.
            unreadable.Add(stored, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-announced {EventType} of stream {StreamId} at version {StreamVersion}, stored at {StoredAt} without its announcement.")]
    private static partial void LogReannounced(ILogger logger, string eventType, Guid streamId, long streamVersion, DateTimeOffset storedAt);
}
