using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Every path that takes an approval away says who did it and why (#246): a changed test blueprint, an edited
/// question and a replaced chapter each raise one <see cref="QuestionApprovalsReset"/> naming the editor who made the
/// change and the reason, but only when at least one approved question went back to unreviewed. Nothing is raised when
/// nothing approved was reset, when nothing changed, or when the change was refused. The resetting editor here is the
/// reviewer, who approved nothing, so the event names the editor passed in and not an approver.
/// </summary>
public class ApprovalResetEventTests
{
    [Fact]
    public void Changing_the_test_settings_raises_one_reset_by_the_editor_for_TestSettingsChanged()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out _);
        var second = version.AddQuestion(QuestionId.New(), MultipleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(second, Approver, ApproversName, Now).IsSuccess.ShouldBeTrue();

        var set = version.SetBlueprint(Blueprint(10, 81, 12), Reviewer);

        set.IsSuccess.ShouldBeTrue();
        Resets(version).ShouldHaveSingleItem().ShouldBe(new QuestionApprovalsReset(version.Id, Reviewer, ApprovalResetReason.TestSettingsChanged));
    }

    [Fact]
    public void Changing_the_test_settings_with_no_approved_question_raises_no_reset()
    {
        var version = DraftWithQuestion(out _, out _);

        var set = version.SetBlueprint(Blueprint(10, 81, 12), Reviewer);

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(Blueprint(10, 81, 12));
        Resets(version).ShouldBeEmpty();
    }

    [Fact]
    public void Saving_the_same_test_settings_again_raises_no_reset()
    {
        var version = DraftWithApprovedQuestion(out _, out _);

        var set = version.SetBlueprint(Blueprint(10, 80, 12), Reviewer);

        set.IsSuccess.ShouldBeTrue();
        version.Questions.ShouldHaveSingleItem().Review.ShouldBe(ReviewState.Approved);
        Resets(version).ShouldBeEmpty();
    }

    [Fact]
    public void Editing_an_approved_question_raises_a_reset_by_the_editor_for_QuestionEdited()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out var question);

        var edited = version.EditQuestion(question, MultipleChoice(chapter), Reviewer, AnswerOptionId.New);

        edited.IsSuccess.ShouldBeTrue();
        version.Questions.ShouldHaveSingleItem().Review.ShouldBe(ReviewState.Unreviewed);
        Resets(version).ShouldHaveSingleItem().ShouldBe(new QuestionApprovalsReset(version.Id, Reviewer, ApprovalResetReason.QuestionEdited));
    }

    [Fact]
    public void Editing_an_unreviewed_question_raises_no_reset()
    {
        var version = DraftWithQuestion(out var chapter, out var question);

        var edited = version.EditQuestion(question, MultipleChoice(chapter), Reviewer, AnswerOptionId.New);

        edited.IsSuccess.ShouldBeTrue();
        Resets(version).ShouldBeEmpty();
    }

    [Fact]
    public void Replacing_a_chapter_with_an_approved_question_raises_a_reset_by_the_editor_for_ChapterReplaced()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out _);

        var replaced = version.ReplaceChapter(TextChapter(chapter, "Händehygiene", "Neu: vor und nach …"), Reviewer);

        replaced.IsSuccess.ShouldBeTrue();
        version.Questions.ShouldHaveSingleItem().Review.ShouldBe(ReviewState.Unreviewed);
        Resets(version).ShouldHaveSingleItem().ShouldBe(new QuestionApprovalsReset(version.Id, Reviewer, ApprovalResetReason.ChapterReplaced));
    }

    /// <summary>An approved question about another chapter keeps its approval, so replacing this one resets nothing approved.</summary>
    [Fact]
    public void Replacing_a_chapter_without_approved_questions_raises_no_reset()
    {
        var version = DraftWithApprovedQuestion(out _, out var approved);
        var other = ChapterId.New();
        version.AddChapter(TextChapter(other, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        version.AddQuestion(QuestionId.New(), SingleChoice(other), AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var replaced = version.ReplaceChapter(TextChapter(other, "Flächendesinfektion", "Neu: wischen."), Reviewer);

        replaced.IsSuccess.ShouldBeTrue();
        version.Questions.Single(question => question.Id == approved).Review.ShouldBe(ReviewState.Approved);
        Resets(version).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("set the test settings")]
    [InlineData("edit a question")]
    [InlineData("replace a chapter")]
    public void A_refused_change_raises_no_reset(string change)
    {
        var version = InReview(out var chapter);
        var question = version.Questions.ShouldHaveSingleItem();

        var refused = change switch
        {
            "set the test settings" => version.SetBlueprint(Blueprint(1, 81, 12), Reviewer),
            "edit a question" => version.EditQuestion(question.Id, MultipleChoice(chapter), Reviewer, AnswerOptionId.New),
            "replace a chapter" => version.ReplaceChapter(TextChapter(chapter, "Händehygiene", "Neu: vor und nach …"), Reviewer),
            _ => throw new InvalidOperationException($"No change is called '{change}'."),
        };

        refused.IsFailure.ShouldBeTrue($"{change} on a version in review");
        refused.Error.ShouldBeOfType<NotADraft>();
        question.Review.ShouldBe(ReviewState.Approved);
        Resets(version).ShouldBeEmpty();
    }

    private static IReadOnlyList<QuestionApprovalsReset> Resets(CourseVersion version) => [.. version.DomainEvents.OfType<QuestionApprovalsReset>()];

    private static TestBlueprint Blueprint(int questions, int passingScore, int validityMonths) =>
        new(QuestionCount.From(questions), ScorePercent.From(passingScore), RecurrenceInterval.From(validityMonths));
}
