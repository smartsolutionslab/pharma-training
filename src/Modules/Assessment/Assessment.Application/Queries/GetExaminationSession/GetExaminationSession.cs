using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationSession;

/// <summary>The learning session of the learner's own examination, by which the client reads it; another person's is not found.</summary>
public sealed record GetExaminationSession(Guid ExaminationId, Guid Learner) : IQuery<Result<Guid, ApiError>>;
