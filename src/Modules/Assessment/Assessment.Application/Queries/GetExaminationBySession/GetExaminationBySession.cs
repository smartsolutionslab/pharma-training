using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationBySession;

/// <summary>The learner's own examination of a learning session; another person's is not found.</summary>
public sealed record GetExaminationBySession(Guid SessionId, Guid Learner) : IQuery<Result<AssessmentExaminationDto, ApiError>>;
