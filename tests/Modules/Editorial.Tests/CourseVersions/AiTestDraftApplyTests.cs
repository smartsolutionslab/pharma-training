using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Applying an AI test draft to a version (#22): the version names the questions it drafted, in the draft's order, so
/// the use case can answer with them; a refused draft names none and changes nothing. Drafted questions are unusable
/// until approved: a version holding one can be submitted, but not published.
/// </summary>
public class AiTestDraftApplyTests
{
    [Fact]
    public void Applying_a_draft_returns_the_ids_of_the_drafted_questions_in_their_order()
    {
        var version = DraftWithQuestion(out var chapter, out var approved);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(3), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(approved, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        var first = QuestionId.New();
        var second = QuestionId.New();
        var handedOut = new Queue<QuestionId>([first, second]);

        var applied = version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter)], handedOut.Dequeue, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        applied.Value.ShouldBe([first, second]);
        version.Questions.Select(question => question.Id).ShouldBe([approved, first, second]);
        version.Questions.Skip(1).ShouldAllBe(question => question.Provenance == Provenance.AiDrafted && question.Review == ReviewState.Unreviewed);
    }

    [Fact]
    public void A_refused_draft_returns_no_ids_and_changes_nothing()
    {
        var version = InReview(out var chapter);
        List<QuestionId> before = [.. version.Questions.Select(question => question.Id)];

        var applied = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.ShouldBeOfType<NotADraft>();
        version.Questions.Select(question => question.Id).ShouldBe(before);
    }

    [Fact]
    public void Ai_drafted_questions_keep_the_version_from_being_published_until_each_is_approved()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(2), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var first = QuestionId.New();
        var second = QuestionId.New();
        var handedOut = new Queue<QuestionId>([first, second]);
        version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter)], handedOut.Dequeue, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue("submitting does not ask for approval");
        var refused = version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Value.ShouldBeOfType<QuestionNotApproved>().Question.ShouldBe(first);
        version.Status.ShouldBe(CourseVersionStatus.InReview);

        version.ApproveQuestion(first, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(second, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();
        var published = version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
    }
}
