using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetCurrentAttempt;

/// <summary>The open attempt of the learner's own examination, as its client shows it.</summary>
public sealed record GetCurrentAttempt(Guid ExaminationId, Guid Learner) : IQuery<Result<AssessmentAttemptDto, ApiError>>;
