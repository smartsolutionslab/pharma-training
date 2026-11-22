using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A version holds at most <see cref="QuestionCount.Maximum"/> questions: one more is refused as a rule of the version,
/// so submitting never meets a count it cannot name and answers TestIncomplete instead of throwing.
/// </summary>
public class QuestionLimitTests
{
    [Fact]
    public void A_question_beyond_the_maximum_is_refused_and_the_count_stays_at_the_maximum()
    {
        var version = AtTheMaximum(out var chapter);

        var added = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New);

        added.IsFailure.ShouldBeTrue();
        added.Error.Code.ShouldBe("editorial.too-many-questions");
        version.Questions.Count.ShouldBe(QuestionCount.Maximum);
    }

    [Fact]
    public void A_version_at_the_maximum_whose_blueprint_asks_for_fewer_reports_the_test_incomplete()
    {
        var version = AtTheMaximum(out _);

        var submitted = version.SubmitForReview(Author);

        var incomplete = submitted.Error.ShouldBeOfType<TestIncomplete>();
        incomplete.Expected.ShouldBe(TestBlueprint.Default.Questions);
        incomplete.Actual.ShouldBe(QuestionCount.From(QuestionCount.Maximum));
    }

    [Fact]
    public void A_version_at_the_maximum_whose_blueprint_asks_for_the_maximum_is_submitted()
    {
        var version = AtTheMaximum(out _);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(QuestionCount.Maximum), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();

        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.InReview);
    }

    [Fact]
    public void Submitting_after_a_question_beyond_the_maximum_was_tried_reports_the_test_incomplete_and_never_throws()
    {
        var version = AtTheMaximum(out var chapter);
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New);

        var submitted = Should.NotThrow(() => version.SubmitForReview(Author));

        submitted.Error.ShouldBeOfType<TestIncomplete>().Actual.ShouldBe(QuestionCount.From(QuestionCount.Maximum));
    }

    [Fact]
    public void Submitting_a_draft_loaded_with_more_than_the_maximum_fails_with_too_many_questions_and_never_throws()
    {
        var version = AtTheMaximum(out _);
        var stray = DraftWithChapter(out var strayChapter);
        stray.AddQuestion(QuestionId.New(), SingleChoice(strayChapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        // Rows written before the cap, or by hand, are loaded by EF Core through the backing list, past AddQuestion's
        // check. The list behind Questions is that backing list, so adding to it stands in for such a load.
        var loaded = version.Questions.ShouldBeOfType<List<Question>>();
        loaded.Add(stray.Questions[0]);
        version.Questions.Count.ShouldBe(QuestionCount.Maximum + 1);

        var submitted = Should.NotThrow(() => version.SubmitForReview(Author));

        submitted.IsFailure.ShouldBeTrue();
        submitted.Error.ShouldBeOfType<TooManyQuestions>().Code.ShouldBe("editorial.too-many-questions");
        version.Status.ShouldBe(CourseVersionStatus.Draft);
    }

    /// <summary>A draft with one chapter and exactly <see cref="QuestionCount.Maximum"/> questions, its blueprint the default.</summary>
    private static CourseVersion AtTheMaximum(out ChapterId chapter)
    {
        var version = DraftWithChapter(out chapter);
        for (var added = 0; added < QuestionCount.Maximum; added++)
        {
            version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        }

        return version;
    }
}
