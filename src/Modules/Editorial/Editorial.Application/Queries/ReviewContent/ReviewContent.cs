using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ReviewContent;

/// <summary>An AI review of one course version's text chapters, with hints per chapter; it changes nothing. The empty Guid is no version's.</summary>
public sealed record ReviewContent(Guid Id) : IQuery<Result<EditorialContentReviewDto, ApiError>>;
