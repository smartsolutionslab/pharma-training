using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.AnswerQuestion;

/// <summary>The learner answers a question of the open attempt of their own examination; a later answer replaces an earlier one.</summary>
public sealed record AnswerQuestion(ExaminationId Examination, PersonId Learner, QuestionVersionId Question, IReadOnlyList<AnswerOptionId> Chosen)
    : ICommand<Result<Unit, ApiError>>;
