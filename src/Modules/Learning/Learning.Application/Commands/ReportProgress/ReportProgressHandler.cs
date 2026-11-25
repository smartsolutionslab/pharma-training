using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReportProgress;

/// <summary>
/// Records the reported progress on the learner's own session. Only an active member of any pharmacy may report it
/// (docs/adr/0017, #242): anyone else, a former member on their own session included, is refused with
/// learning.membership-missing before the session is loaded, and nothing is stored, so a non-member is refused alike
/// for any well-formed session id. The endpoint answers before the handler runs: an empty session id with 404, a bad
/// percent or chapter with 400. The check reads Learning's eventually consistent copy of the memberships, so a report
/// processed while an ending is still on its way may land, the window ADR 0017 accepts, as for starting a session.
/// For an active member another person's session is not found, like an unknown one. A report that adds nothing stores
/// nothing. The session is stored before its ContentCompleted, raised once per cycle, is published as
/// ContentCompletedV1 and committed, with a marker per announced completion in the same commit (#277): a completion
/// stored without its marker, because that commit never happened, is announced by the module's announcement sweep
/// (ReannounceContentCompletions, docs/adr/0016). A report that lost the append race to another one answers
/// learning.session-changed and publishes nothing; the server does not retry it. A video report faster than playback,
/// judged against the duration in Learning's copy of the session's version, answers learning.progress-too-fast and
/// stores nothing (ADR 0024).
/// </summary>
public sealed class ReportProgressHandler(
    IMembershipCopies memberships,
    ILearningAggregateStore store,
    ILearningUnitOfWork unitOfWork,
    IAnnouncedEvents announced,
    ICourseVersionCopies versions,
    TimeProvider time)
    : ICommandHandler<ReportProgress, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ReportProgress command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        // Only an active member reports progress (docs/adr/0017); it is checked before the session is loaded, so a
        // non-member is refused alike for any well-formed id and learns nothing of the sessions Learning holds.
        var access = await LearnerAccess.RequireActiveMemberAsync(memberships, command.Learner, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var loaded = await store.LoadAsync<LearningSession>(command.Session, cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner != command.Learner)
        {
            return Failure(LearningSessionFailures.SessionNotFound(command.Session.Value));
        }

        var session = loaded.Value;
        var duration = await versions.FindVideoDurationAsync(session.CourseVersion, command.Chapter, cancellationToken);
        var now = time.GetUtcNow();
        var recorded = session.RecordProgress(command.Chapter, command.Progress, duration, Timestamp.From(now));
        if (recorded.IsFailure)
        {
            return Failure(LearningSessionFailures.From(recorded.Error));
        }

        // Saving clears the pending events, so what is announced is taken before, with the version Marten stores each
        // at: the session's version before the save plus its place among the pending events, counted from 1.
        var sourced = (IEventSourced)session;
        var pending = sourced.PendingEvents;
        if (pending.Count == 0)
        {
            return Success(Unit.Value);
        }

        var completed = pending
            .Select((pendingEvent, index) => (Event: pendingEvent, StreamVersion: sourced.Version + index + 1))
            .Where(stored => stored.Event is ContentCompleted)
            .Select(stored => (Completed: (ContentCompleted)stored.Event, stored.StreamVersion))
            .ToList();
        // Of two reports that loaded the same version of the stream, only one append lands; the other one stores and
        // announces nothing.
        var saved = await store.TrySaveAsync(session, cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(LearningSessionFailures.LostRace(saved.Error, command.Session.Value));
        }

        // The append has landed: a caller who goes away now must not cancel the announcement, or it is lost for good
        // (no later report stores anything again), so the outbox commit no longer takes the request's token. Each
        // announcement's marker is committed with it, so the sweep never announces it again.
        if (completed.Count > 0)
        {
            foreach (var (content, streamVersion) in completed)
            {
                await unitOfWork.PublishAsync(content.ToIntegrationEvent());
                announced.Add(new AnnouncedEvent { StreamId = command.Session.Value, StreamVersion = streamVersion, AnnouncedAt = now });
            }

            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        return Success(Unit.Value);
    }
}
