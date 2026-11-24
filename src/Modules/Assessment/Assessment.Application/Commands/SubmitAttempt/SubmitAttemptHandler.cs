using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.SubmitAttempt;

/// <summary>
/// Scores the open attempt of the learner's own examination on the server against the version's copy; another
/// person's examination is not found, like an unknown one. The examination is stored first; then, in the order the
/// stream holds them, a pass is published as TestPassedV1, a failure as TestFailedV1, and the failure of the last
/// allowed attempt also as AttemptsExhaustedV1 with every chapter failed since the last unlock, and all are committed
/// together. A submit that lost the append race answers assessment.examination-changed and stores and publishes
/// nothing; the server does not retry it (docs/adr/0016).
/// </summary>
public sealed class SubmitAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, IAssessmentUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<SubmitAttempt, Result<AttemptId, ApiError>>
{
    public async Task<Result<AttemptId, ApiError>> HandleAsync(SubmitAttempt command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var loaded = await store.LoadAsync<Examination>(command.Examination, cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner != command.Learner)
        {
            return Failure(ExaminationFailures.NotFound(command.Examination.Value));
        }

        var examination = loaded.Value;
        if (!examination.CurrentAttempt.HasValue)
        {
            return Failure(ExaminationFailures.From(ExaminationErrors.NoOpenAttempt(examination.Id)));
        }

        var attempt = examination.CurrentAttempt.Value.Id;

        // An attempt started on the copy, and a copy is never removed: a missing one is a broken store.
        var copy = await copies.FindAsync(examination.CourseVersion, cancellationToken);
        if (!copy.HasValue)
        {
            throw new InvalidOperationException($"The test definition of course version {examination.CourseVersion} is gone, although examination {examination.Id} started an attempt on it.");
        }

        var submitted = examination.Submit(copy.Value.ToDefinition(), Timestamp.From(time.GetUtcNow()));
        if (submitted.IsFailure)
        {
            return Failure(ExaminationFailures.From(submitted.Error));
        }

        // Saving clears the pending events, so what is announced is taken before.
        var announcements = AnnouncementsOf(examination);
        if (!await store.TrySaveAsync(examination, cancellationToken))
        {
            return Failure(ExaminationFailures.Changed(command.Examination.Value));
        }

        // The append has landed: a caller who goes away now must not cancel the announcement, or it is lost for good,
        // so the outbox commit no longer takes the request's token.
        if (announcements.Count > 0)
        {
            foreach (var announcement in announcements)
            {
                await unitOfWork.PublishAsync(announcement);
            }

            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        return Success(attempt);
    }

    /// <summary>What the examination's pending events announce to other modules, in the order the stream will hold them.</summary>
    private static List<IntegrationEvent> AnnouncementsOf(Examination examination)
    {
        List<IntegrationEvent> announcements = [];
        foreach (var pending in ((IEventSourced)examination).PendingEvents)
        {
            if (pending is ExaminationPassed passed)
            {
                announcements.Add(passed.ToIntegrationEvent());
            }
            else if (pending is AttemptSubmitted { Outcome: AttemptOutcome.Failed } failed)
            {
                announcements.Add(failed.ToIntegrationEvent(examination));
            }
            else if (pending is ExaminationExhausted exhausted)
            {
                announcements.Add(exhausted.ToIntegrationEvent(examination));
            }
        }

        return announcements;
    }
}
