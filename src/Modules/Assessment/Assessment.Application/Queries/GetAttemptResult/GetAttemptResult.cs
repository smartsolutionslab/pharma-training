using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetAttemptResult;

/// <summary>A submitted attempt's result of the learner's own examination: the score, the outcome and the chapters to repeat, never a solution.</summary>
public sealed record GetAttemptResult(Guid ExaminationId, Guid AttemptId, Guid Learner) : IQuery<Result<AssessmentAttemptResultDto, ApiError>>;
