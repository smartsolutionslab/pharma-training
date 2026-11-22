using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.RequestChapterUpload;

/// <summary>A short-lived URL that uploads one PDF or video for a chapter of a draft, under a name the module chooses.</summary>
public sealed record RequestChapterUpload(Guid Version, EditorialChapterKind Kind) : IQuery<Result<EditorialChapterUploadDto, ApiError>>;
