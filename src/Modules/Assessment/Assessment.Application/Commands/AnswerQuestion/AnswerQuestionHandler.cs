using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.AnswerQuestion;

/// <summary>
/// Records the learner's answer to a question of the open attempt of their own examination; another person's
/// examination is not found, like an unknown one. The domain refuses first (no open attempt, a question outside it);
/// only then are the options checked against the version's copy, which the aggregate does not know. A refused answer
/// stores nothing, and one that lost the append race answers assessment.examination-changed.
/// </summary>
public sealed class AnswerQuestionHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, TimeProvider time)
    : ICommandHandler<AnswerQuestion, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(AnswerQuestion command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var loaded = await store.LoadAsync<Examination>(command.Examination, cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner != command.Learner)
        {
            return Failure(ExaminationFailures.NotFound(command.Examination.Value));
        }

        var examination = loaded.Value;
        var answered = examination.Answer(command.Question, command.Chosen, Timestamp.From(time.GetUtcNow()));
        if (answered.IsFailure)
        {
            return Failure(ExaminationFailures.From(answered.Error));
        }

        // The aggregate and its pending answer are discarded with the request when the options are refused.
        var copy = await copies.FindAsync(examination.CourseVersion, cancellationToken);
        var question = copy.HasValue ? copy.Value.Questions.SingleOrDefault(candidate => candidate.QuestionId == command.Question.Value) : null;
        if (question is null)
        {
            throw new InvalidOperationException(
                $"The question {command.Question} of the open attempt of examination {command.Examination} is not in the copy of course version {examination.CourseVersion}.");
        }

        var refused = Refusal(question, command.Chosen);
        if (refused.HasValue)
        {
            return Failure(refused.Value);
        }

        if (!await store.TrySaveAsync(examination, cancellationToken))
        {
            return Failure(ExaminationFailures.Changed(command.Examination.Value));
        }

        return Success(Unit.Value);
    }

    private static Option<ApiError> Refusal(TestQuestionCopy question, IReadOnlyList<AnswerOptionId> chosen)
    {
        var options = question.Options.Select(option => option.OptionId).ToHashSet();
        var foreign = chosen.FirstOrDefault(option => !options.Contains(option.Value));
        if (foreign is not null)
        {
            return Option<ApiError>.Some(ExaminationFailures.OptionNotInQuestion(question.QuestionId, foreign.Value));
        }

        if (question.Type == TestQuestionType.SingleChoice && chosen.Count > 1)
        {
            return Option<ApiError>.Some(ExaminationFailures.SingleChoiceTakesOneOption(question.QuestionId));
        }

        return Option<ApiError>.None;
    }
}
