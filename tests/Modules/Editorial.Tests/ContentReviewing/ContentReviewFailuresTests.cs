using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>The AI content review's own failures: each a stable code with its status, and one English message per code.</summary>
public class ContentReviewFailuresTests
{
    [Fact]
    public void Each_failure_has_its_code_status_and_one_English_message()
    {
        Failure(ContentReviewFailures.NoTextChapters, "editorial.no-text-chapters-to-review", HttpStatusCode.Conflict);
        Failure(ContentReviewFailures.TooMuchContent, "editorial.too-much-content-to-review", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public void Each_failure_is_its_own_record()
    {
        ContentReviewFailures.NoTextChapters().ShouldBeOfType<NoTextChaptersToReview>();
        ContentReviewFailures.TooMuchContent().ShouldBeOfType<TooMuchContentToReview>();
    }

    private static void Failure(Func<ApiError> failure, string code, HttpStatusCode status)
    {
        var error = failure();

        error.Code.ShouldBe(code);
        error.Status.ShouldBe(status);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        failure().Message.ShouldBe(error.Message, "a code has one message");
    }
}
