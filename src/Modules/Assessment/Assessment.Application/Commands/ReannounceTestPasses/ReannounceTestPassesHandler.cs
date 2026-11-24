using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestPasses;

/// <summary>
/// One pass of Assessment's announcement sweep (#277, docs/adr/0016). A submit stores its ExaminationPassed before it
/// commits the announcement; a crash or failure in between leaves the pass stored and never announced. Every stored
/// ExaminationPassed without a marker, stored between the lookback and the grace period ago, is announced as the submit
/// would have announced it (TestPassedV1, the same mapping, so PassedAt is the stored one), oldest first and at most a
/// batch a pass, each with its marker in a commit of its own, and logged as a Warning naming the stream and version. Of
/// two writers that announce the same event (a replica's sweep, the late submit), the marker's primary key lets one
/// commit through; the other throws, and the hosted service logs it. The pass has no failure of its own: anything else
/// throws.
/// </summary>
public sealed partial class ReannounceTestPassesHandler(
    IUnannouncedTestPasses unannounced,
    IAnnouncedEvents announced,
    IAssessmentUnitOfWork unitOfWork,
    AnnouncementSweepSettings settings,
    TimeProvider time,
    ILogger<ReannounceTestPassesHandler> logger)
    : ICommandHandler<ReannounceTestPasses, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceTestPasses command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var now = time.GetUtcNow();
        var found = await unannounced.FindAsync(now - settings.Lookback, now - settings.GracePeriod, settings.BatchSize, cancellationToken);
        foreach (var stored in found)
        {
            var passed = await unannounced.LoadAsync(stored, cancellationToken);
            var stream = passed.Examination.Value;
            await unitOfWork.PublishAsync(passed.ToIntegrationEvent());
            announced.Add(new AnnouncedEvent { StreamId = stream, StreamVersion = stored.StreamVersion, AnnouncedAt = now });
            LogReannounced(logger, nameof(Domain.Examinations.Events.ExaminationPassed), stream, stored.StreamVersion, stored.StoredAt);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Success(Unit.Value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-announced {EventType} of stream {StreamId} at version {StreamVersion}, stored at {StoredAt} without its announcement.")]
    private static partial void LogReannounced(ILogger logger, string eventType, Guid streamId, long streamVersion, DateTimeOffset storedAt);
}
