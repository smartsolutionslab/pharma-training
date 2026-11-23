using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.StartAttempt;

/// <summary>
/// Starts an attempt at the learner's own examination, its questions in the order derived from the new attempt's id
/// (ADR 0022). Another person's examination is not found, like an unknown one. The version's test definition must
/// have arrived (ADR 0012). A start that lost the append race answers assessment.examination-changed and stores nothing.
/// </summary>
public sealed class StartAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, TimeProvider time)
    : ICommandHandler<StartAttempt, Result<AttemptId, ApiError>>
{
    public async Task<Result<AttemptId, ApiError>> HandleAsync(StartAttempt command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var loaded = await store.LoadAsync<Examination>(command.Examination, cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner != command.Learner)
        {
            return Failure(ExaminationFailures.NotFound(command.Examination.Value));
        }

        var examination = loaded.Value;
        var copy = await copies.FindAsync(examination.CourseVersion, cancellationToken);
        if (!copy.HasValue)
        {
            return Failure(ExaminationFailures.TestDefinitionMissing(examination.CourseVersion.Value));
        }

        var attempt = AttemptId.New();
        var order = AttemptShuffle.Questions(attempt, copy.Value.Questions.Select(question => QuestionVersionId.From(question.QuestionId)));
        var started = examination.StartAttempt(attempt, order, Timestamp.From(time.GetUtcNow()));
        if (started.IsFailure)
        {
            return Failure(ExaminationFailures.From(started.Error));
        }

        if (!await store.TrySaveAsync(examination, cancellationToken))
        {
            return Failure(ExaminationFailures.Changed(command.Examination.Value));
        }

        return Success(attempt);
    }
}
