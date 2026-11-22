using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>What a caller gets wrong in a question it sends, refused before anything is loaded, each with its own code.</summary>
public class QuestionRequestFailuresTests
{
    [Fact]
    public void A_question_text_that_is_none_is_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidQuestionText();

        failure.ShouldBe(new InvalidQuestionText());
        failure.Code.ShouldBe("editorial.invalid-question-text");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_question_type_that_is_none_is_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidQuestionType();

        failure.ShouldBe(new InvalidQuestionType());
        failure.Code.ShouldBe("editorial.invalid-question-type");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Answer_options_that_are_none_are_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidAnswerOptions();

        failure.ShouldBe(new InvalidAnswerOptions());
        failure.Code.ShouldBe("editorial.invalid-answer-options");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void An_answer_text_that_is_none_is_a_bad_request()
    {
        var failure = CourseVersionFailures.InvalidAnswerText();

        failure.ShouldBe(new InvalidAnswerText());
        failure.Code.ShouldBe("editorial.invalid-answer-text");
        failure.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void A_question_id_no_question_can_have_is_not_found_with_the_code_the_domain_gives_a_missing_question()
    {
        var version = Guid.CreateVersion7();

        var failure = CourseVersionFailures.UnknownQuestion(version, Guid.Empty);

        failure.ShouldBe(new UnknownQuestion(version, Guid.Empty));
        failure.Code.ShouldBe("editorial.question-not-found");
        failure.Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
