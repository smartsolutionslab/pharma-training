using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>Everything the AI content review refuses, each with its own code.</summary>
public static class ContentReviewFailures
{
    public static ApiError NoTextChapters() => new NoTextChaptersToReview();

    public static ApiError TooMuchContent() => new TooMuchContentToReview();

    public static ApiError Malformed(ContentReviewDefect defect) => new AiReviewMalformed(defect);
}
