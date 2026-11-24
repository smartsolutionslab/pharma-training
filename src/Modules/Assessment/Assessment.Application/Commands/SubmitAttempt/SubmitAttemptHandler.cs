using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.SubmitAttempt;

/// <summary>
/// Scores the open attempt of the learner's own examination on the server against the version's copy; another
/// person's examination is not found, like an unknown one. The examination is stored first; a pass is then published
/// as TestPassedV1 for Learning's session and committed, with its marker in the same commit (#277): a pass stored
/// without its marker, because that commit never happened, is announced by the module's announcement sweep
/// (ReannounceTestPasses, docs/adr/0016). A submit that lost the append race answers
/// assessment.examination-changed and stores and publishes nothing; the server does not retry it.
/// </summary>
public sealed class SubmitAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, IAssessmentUnitOfWork unitOfWork, IAnnouncedEvents announced, TimeProvider time)
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

        var now = time.GetUtcNow();
        var submitted = examination.Submit(copy.Value.ToDefinition(), Timestamp.From(now));
        if (submitted.IsFailure)
        {
            return Failure(ExaminationFailures.From(submitted.Error));
        }

        // Saving clears the pending events, so what is announced is taken before, with the version Marten stores each
        // at: the examination's version before the save plus its place among the pending events, counted from 1.
        var sourced = (IEventSourced)examination;
        var passed = sourced.PendingEvents
            .Select((pending, index) => (Event: pending, StreamVersion: sourced.Version + index + 1))
            .Where(stored => stored.Event is ExaminationPassed)
            .Select(stored => (Passed: (ExaminationPassed)stored.Event, stored.StreamVersion))
            .ToList();
        if (!await store.TrySaveAsync(examination, cancellationToken))
        {
            return Failure(ExaminationFailures.Changed(command.Examination.Value));
        }

        // The append has landed: a caller who goes away now must not cancel the announcement, or it is lost for good,
        // so the outbox commit no longer takes the request's token. The announcement's marker is committed with it, so
        // the sweep never announces it again.
        if (passed.Count > 0)
        {
            foreach (var (pass, streamVersion) in passed)
            {
                await unitOfWork.PublishAsync(pass.ToIntegrationEvent());
                announced.Add(new AnnouncedEvent { StreamId = command.Examination.Value, StreamVersion = streamVersion, AnnouncedAt = now });
            }

            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        return Success(attempt);
    }
}
