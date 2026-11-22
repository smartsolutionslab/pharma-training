using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>What a caller gets wrong in a blueprint it sends, refused before anything is loaded, each with its own code.</summary>
public class BlueprintRequestFailuresTests
{
    [Fact]
    public void A_question_count_out_of_range_is_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidQuestionCount();

        failure.ShouldBe(new InvalidQuestionCount());
        failure.Code.ShouldBe("editorial.invalid-question-count");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
        failure.Message.ShouldBe("A test has 1 to 50 questions.");
    }

    [Fact]
    public void A_passing_score_out_of_range_is_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidPassingScore();

        failure.ShouldBe(new InvalidPassingScore());
        failure.Code.ShouldBe("editorial.invalid-passing-score");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
        failure.Message.ShouldBe("A passing score is 0 to 100 percent.");
    }

    [Fact]
    public void A_validity_out_of_range_is_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidValidity();

        failure.ShouldBe(new InvalidValidity());
        failure.Code.ShouldBe("editorial.invalid-validity");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
        failure.Message.ShouldBe("A certificate is valid for 1 to 60 months.");
    }
}
