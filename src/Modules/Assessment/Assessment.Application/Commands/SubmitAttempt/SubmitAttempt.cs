using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.SubmitAttempt;

/// <summary>The learner submits the open attempt of their own examination; the server scores it.</summary>
public sealed record SubmitAttempt(ExaminationId Examination, PersonId Learner) : ICommand<Result<AttemptId, ApiError>>;
