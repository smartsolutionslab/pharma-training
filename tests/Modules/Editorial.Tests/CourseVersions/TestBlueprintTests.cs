using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A blueprint is made through <c>TryCreate</c>, which reports a test with too few questions as a domain error instead of
/// throwing, so the endpoint no longer checks <see cref="TestBlueprint.MinimumQuestions"/> itself.
/// </summary>
public class TestBlueprintTests
{
    [Fact]
    public void A_blueprint_with_fewer_than_the_minimum_questions_fails_with_the_code_the_endpoint_answers()
    {
        var created = TestBlueprint.TryCreate(QuestionCount.From(TestBlueprint.MinimumQuestions - 1), ScorePercent.From(80), RecurrenceInterval.Yearly);

        created.IsFailure.ShouldBeTrue();
        created.Error.Code.ShouldBe("editorial.invalid-question-count");
    }

    [Theory]
    [InlineData(TestBlueprint.MinimumQuestions, 0, RecurrenceInterval.MinimumMonths)]
    [InlineData(10, 80, 12)]
    [InlineData(QuestionCount.Maximum, 100, RecurrenceInterval.MaximumMonths)]
    public void A_blueprint_with_values_in_range_is_created_with_them(int questions, int passingScore, int validityMonths)
    {
        var created = TestBlueprint.TryCreate(QuestionCount.From(questions), ScorePercent.From(passingScore), RecurrenceInterval.From(validityMonths));

        created.IsSuccess.ShouldBeTrue();
        created.Value.Questions.ShouldBe(QuestionCount.From(questions));
        created.Value.PassingScore.ShouldBe(ScorePercent.From(passingScore));
        created.Value.Validity.ShouldBe(RecurrenceInterval.From(validityMonths));
    }
}
