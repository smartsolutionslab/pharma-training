using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ReviewContent;

/// <summary>
/// Loads the version and reviews it with the model (ADR 0009). It takes no unit of work, so it cannot save: the version
/// is read and never changed.
/// </summary>
public sealed class ReviewContentHandler(ICourseVersionStore versions, IChatClient model)
    : IQueryHandler<ReviewContent, Result<EditorialContentReviewDto, ApiError>>
{
    public async Task<Result<EditorialContentReviewDto, ApiError>> HandleAsync(ReviewContent query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(CourseVersionFailures.NotFound(query.Id));
        }

        var version = await versions.FindAsync(CourseVersionId.From(query.Id), cancellationToken);
        if (!version.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(query.Id));
        }

        var review = await ContentReviewer.ReviewAsync(model, version.Value, cancellationToken);
        if (review.IsFailure)
        {
            return Failure(review.Error);
        }

        return Success(review.Value.ToDto());
    }
}
