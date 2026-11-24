using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestPasses;

/// <summary>
/// One pass of Assessment's announcement sweep (#277, docs/adr/0016). A submit stores its ExaminationPassed before it
/// commits the announcement; a crash or failure in between leaves the pass stored and never announced. Every stored
/// ExaminationPassed without a marker, stored between the lookback and the grace period ago, is announced as the submit
/// would have announced it (TestPassedV1, the same mapping, so PassedAt is the stored one), oldest first and at most a
/// batch a pass, each with its marker, keyed on the stream the event is stored in, in a commit of its own, and logged
/// as a Warning naming the stream and version. One that cannot be read or mapped is skipped, and the pass reads on past
/// it: it reads a page of a batch at a time, each after the last event it read, until it has announced a batch, reached
/// the end of the window or read AnnouncementSweepSettings.MostPagesPerPass pages (#337). The pass then logs one
/// Warning for all it skipped, naming how many, the first and the last, with the first failure attached (#360). Of two
/// writers that announce the same event (a replica's sweep, the late submit), the marker's primary key lets one commit
/// through; the other throws, and the hosted service logs it. The pass has no failure of its own: anything else throws.
/// </summary>
public sealed partial class ReannounceTestPassesHandler(
    IUnannouncedTestPasses unannounced,
    IAnnouncedEvents announced,
    IAssessmentUnitOfWork unitOfWork,
    [FromKeyedServices(typeof(ReannounceTestPasses))] AnnouncementSweepSettings settings,
    TimeProvider time,
    ILogger<ReannounceTestPassesHandler> logger)
    : ICommandHandler<ReannounceTestPasses, Result<Unit, ApiError>>
{
    private const string EventType = nameof(Domain.Examinations.Events.ExaminationPassed);

    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceTestPasses command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var now = time.GetUtcNow();
        var storedFrom = now - settings.Lookback;
        var storedUntil = now - settings.GracePeriod;
        var pages = UnannouncedEventPages.ReadAsync(
            (after, limit, token) => unannounced.FindAsync(storedFrom, storedUntil, after, limit, token),
            settings.BatchSize,
            cancellationToken);
        var reannounced = 0;
        var unreadable = new UnreadableEvents(EventType);
        try
        {
            await foreach (var stored in pages)
            {
                var announcement = await AnnouncementOfAsync(stored, unreadable, cancellationToken);
                if (!announcement.HasValue)
                {
                    continue;
                }

                await unitOfWork.PublishAsync(announcement.Value);
                announced.Add(new AnnouncedEvent { StreamId = stored.StreamId, StreamVersion = stored.StreamVersion, AnnouncedAt = now });
                LogReannounced(logger, EventType, stored.StreamId, stored.StreamVersion, stored.StoredAt);
                await unitOfWork.CommitAsync(cancellationToken);
                reannounced++;
                if (reannounced == settings.BatchSize)
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

        return Success(Unit.Value);
    }

    /// <summary>
    /// The announcement of a stored test pass, or none when it cannot be read or mapped: noted for the pass's one
    /// Warning and skipped, and the pass reads on past it. It keeps no marker, so every pass tries it again until the
    /// lookback. A database failure is no unreadable data: it ends the pass.
    /// </summary>
    private async Task<Option<TestPassedV1>> AnnouncementOfAsync(UnannouncedEvent stored, UnreadableEvents unreadable, CancellationToken cancellationToken)
    {
        try
        {
            var passed = await unannounced.LoadAsync(stored, cancellationToken);
            return Option<TestPassedV1>.Some(passed.ToIntegrationEvent());
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or DbException))
        {
            unreadable.Add(stored, exception);
            return Option<TestPassedV1>.None;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-announced {EventType} of stream {StreamId} at version {StreamVersion}, stored at {StoredAt} without its announcement.")]
    private static partial void LogReannounced(ILogger logger, string eventType, Guid streamId, long streamVersion, DateTimeOffset storedAt);
}
