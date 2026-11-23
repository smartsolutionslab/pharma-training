using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetChapterMedia;

/// <summary>A short-lived read URL on Learning's copy of a chapter's PDF or video, for a learner with a session on a version that holds it (#41).</summary>
public sealed record GetChapterMedia(Guid ChapterId, Guid Learner) : IQuery<Result<LearningMediaUrlDto, ApiError>>;
