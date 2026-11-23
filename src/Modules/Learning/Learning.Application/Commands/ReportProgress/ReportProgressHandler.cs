using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReportProgress;

/// <summary>
/// Records the reported progress on the learner's own session; another person's session is not found, like an unknown
/// one. A report that adds nothing stores nothing. The session is stored before its ContentCompleted, raised once per
/// cycle, is published as ContentCompletedV1 and committed. A report that lost the append race to another one answers
/// learning.session-changed and publishes nothing; the server does not retry it. A video report faster than playback,
/// judged against the duration in Learning's copy of the session's version, answers learning.progress-too-fast and
/// stores nothing (ADR 0024).
/// </summary>
public sealed class ReportProgressHandler(ILearningAggregateStore store, ILearningUnitOfWork unitOfWork, ICourseVersionCopies versions, TimeProvider time)
    : ICommandHandler<ReportProgress, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ReportProgress command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var loaded = await store.LoadAsync<LearningSession>(command.Session, cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner != command.Learner)
        {
            return Failure(LearningSessionFailures.SessionNotFound(command.Session.Value));
        }

        var session = loaded.Value;
        var duration = await versions.FindVideoDurationAsync(session.CourseVersion, command.Chapter, cancellationToken);
        var recorded = session.RecordProgress(command.Chapter, command.Progress, duration, Timestamp.From(time.GetUtcNow()));
        if (recorded.IsFailure)
        {
            return Failure(LearningSessionFailures.From(recorded.Error));
        }

        // Saving clears the pending events, so what is announced is taken before.
        var pending = ((IEventSourced)session).PendingEvents;
        if (pending.Count == 0)
        {
            return Success(Unit.Value);
        }

        var completed = pending.OfType<ContentCompleted>().ToList();
        // Of two reports that loaded the same version of the stream, only one append lands; the other one stores and announces nothing.
        if (!await store.TrySaveAsync(session, cancellationToken))
        {
            return Failure(LearningSessionFailures.Changed(command.Session.Value));
        }

        // The append has landed: a caller who goes away now must not cancel the announcement, or it is lost for good
        // (no later report stores anything again), so the outbox commit no longer takes the request's token.
        if (completed.Count > 0)
        {
            foreach (var content in completed)
            {
                await unitOfWork.PublishAsync(content.ToIntegrationEvent());
            }

            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        return Success(Unit.Value);
    }
}
