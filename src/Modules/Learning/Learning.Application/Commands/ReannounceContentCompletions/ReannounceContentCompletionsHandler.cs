using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReannounceContentCompletions;

/// <summary>
/// One pass of Learning's announcement sweep (#277, docs/adr/0016). A report stores its ContentCompleted before it
/// commits the announcement; a crash or failure in between leaves the completion stored and never announced. Every
/// stored ContentCompleted without a marker, stored between the lookback and the grace period ago, is announced as the
/// report would have announced it (ContentCompletedV1, the same mapping), oldest first and at most a batch a pass, each
/// with its marker in a commit of its own, and logged as a Warning naming the stream and version. Of two writers that
/// announce the same event (a replica's sweep, the late report), the marker's primary key lets one commit through; the
/// other throws, and the hosted service logs it. The pass has no failure of its own: anything else throws.
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
    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceContentCompletions command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var now = time.GetUtcNow();
        var found = await unannounced.FindAsync(now - settings.Lookback, now - settings.GracePeriod, settings.BatchSize, cancellationToken);
        foreach (var stored in found)
        {
            var completed = await unannounced.LoadAsync(stored, cancellationToken);
            var stream = completed.Session.Value;
            await unitOfWork.PublishAsync(completed.ToIntegrationEvent());
            announced.Add(new AnnouncedEvent { StreamId = stream, StreamVersion = stored.StreamVersion, AnnouncedAt = now });
            LogReannounced(logger, nameof(Domain.LearningSessions.Events.ContentCompleted), stream, stored.StreamVersion, stored.StoredAt);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Success(Unit.Value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-announced {EventType} of stream {StreamId} at version {StreamVersion}, stored at {StoredAt} without its announcement.")]
    private static partial void LogReannounced(ILogger logger, string eventType, Guid streamId, long streamVersion, DateTimeOffset storedAt);
}
