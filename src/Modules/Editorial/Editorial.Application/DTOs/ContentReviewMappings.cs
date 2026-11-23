using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A checked content review as the module's endpoints answer it.</summary>
public static class ContentReviewMappings
{
    public static EditorialContentReviewDto ToDto(this ContentReview review)
    {
        Ensure.That(review).IsNotNull();
        return new EditorialContentReviewDto([.. review.Reviewed.Select(chapter => chapter.Value)], [.. review.Hints.Select(hint => hint.ToDto())]);
    }
}
