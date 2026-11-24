using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReannounceContentCompletions;

/// <summary>
/// One pass of Learning's announcement sweep (#277, docs/adr/0016). A report stores its ContentCompleted before it
/// commits the announcement; a crash or failure in between leaves the completion stored and never announced. Every
/// stored ContentCompleted without a marker, stored between the lookback and the grace period ago, is announced as the
/// report would have announced it (ContentCompletedV1, the same mapping), oldest first and at most a batch a pass, each
/// with its marker, keyed on the stream the event is stored in, in a commit of its own, and logged as a Warning naming
/// the stream and version. One that cannot be read or mapped is skipped with a Warning, and the pass goes on to the
/// others. Of two writers that announce the same event (a replica's sweep, the late report), the marker's primary key
/// lets one commit through; the other throws, and the hosted service logs it. The pass has no failure of its own:
/// anything else throws.
/// </summary>
public sealed partial class ReannounceContentCompletionsHandler(
    IUnannouncedContentCompletions unannounced,
    IAnnouncedEvents announced,
    ILearningUnitOfWork unitOfWork,
    AnnouncementSweepSettings settings,
    TimeProvider time,
    ILogger<ReannounceContentCompletionsHandler> logger)
    : ICommandHandler<ReannounceContentCompletions, Result<Unit, ApiError>>
{
    private const string EventType = nameof(Domain.LearningSessions.Events.ContentCompleted);

    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceContentCompletions command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var now = time.GetUtcNow();
        var found = await unannounced.FindAsync(now - settings.Lookback, now - settings.GracePeriod, settings.BatchSize, cancellationToken);
        foreach (var stored in found)
        {
            if (await AnnouncementOfAsync(stored, cancellationToken) is not { } announcement)
            {
                continue;
            }

            await unitOfWork.PublishAsync(announcement);
            announced.Add(new AnnouncedEvent { StreamId = stored.StreamId, StreamVersion = stored.StreamVersion, AnnouncedAt = now });
            LogReannounced(logger, EventType, stored.StreamId, stored.StreamVersion, stored.StoredAt);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Success(Unit.Value);
    }

    /// <summary>
    /// The announcement of a stored completion, or null when it cannot be read or mapped: logged and skipped, so one event
    /// that always fails holds up none behind it. It keeps no marker, so every pass tries it again until the lookback.
    /// </summary>
    private async Task<ContentCompletedV1?> AnnouncementOfAsync(UnannouncedEvent stored, CancellationToken cancellationToken)
    {
        try
        {
            var completed = await unannounced.LoadAsync(stored, cancellationToken);
            return completed.ToIntegrationEvent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The type alone: a message about the stored data may quote it, and it is about a learner.
            LogUnreadable(logger, EventType, stored.StreamId, stored.StreamVersion, exception.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {EventType} of stream {StreamId} at version {StreamVersion}: it could not be read or mapped ({Failure}). The next pass tries it again.")]
    private static partial void LogUnreadable(ILogger logger, string eventType, Guid streamId, long streamVersion, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-announced {EventType} of stream {StreamId} at version {StreamVersion}, stored at {StoredAt} without its announcement.")]
    private static partial void LogReannounced(ILogger logger, string eventType, Guid streamId, long streamVersion, DateTimeOffset storedAt);
}
