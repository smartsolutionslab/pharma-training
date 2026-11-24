using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.StartAttempt;

/// <summary>The learner starts an attempt at their own examination.</summary>
public sealed record StartAttempt(ExaminationId Examination, PersonId Learner) : ICommand<Result<AttemptId, ApiError>>;
