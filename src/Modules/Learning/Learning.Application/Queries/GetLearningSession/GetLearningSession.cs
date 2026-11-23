using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetLearningSession;

/// <summary>The learner's own session with its chapters in reading order; another person's is not found.</summary>
public sealed record GetLearningSession(Guid Id, Guid Learner) : IQuery<Result<LearningSessionDto, ApiError>>;
