using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An approval records the reviewer's display name beside the person and the time (#205). It is a historical record:
/// a second approval keeps the first name, a copy into the next version carries it along, and only what takes the
/// approval away (an edit, a changed chapter) clears it with the person and the time.
/// </summary>
public class ApprovalReviewerNameTests
{
    private static readonly Timestamp Later = Timestamp.From(Now.Value.AddHours(3));

    [Fact]
    public void An_approved_question_records_the_reviewers_name_with_its_approver_and_the_time()
    {
        var version = DraftWithQuestion(out _, out var id);

        version.ApproveQuestion(id, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();

        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Approved);
        question.ReviewedBy.ShouldBe(Reviewer);
        question.ReviewedByName.ShouldBe(ReviewersName);
        question.ReviewedAt.ShouldBe(Now);
    }

    [Fact]
    public void Approving_an_approved_question_again_keeps_the_first_reviewers_name()
    {
        var version = DraftWithApprovedQuestion(out _, out var id);

        var approved = version.ApproveQuestion(id, Reviewer, ReviewersName, Later);

        approved.IsFailure.ShouldBeTrue("an approved question was approved again");
        approved.Error.Code.ShouldBe("editorial.question-already-approved");
        var question = version.Questions.ShouldHaveSingleItem();
        question.ReviewedBy.ShouldBe(Author);
        question.ReviewedByName.ShouldBe(AuthorsName);
        question.ReviewedAt.ShouldBe(Now);
    }

    [Fact]
    public void Editing_an_approved_question_clears_the_reviewers_name_and_the_time()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out var id);
        version.Questions.ShouldHaveSingleItem().ReviewedByName.ShouldBe(AuthorsName);

        version.EditQuestion(id, MultipleChoice(chapter), Author, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Unreviewed);
        question.ReviewedBy.ShouldBeNull();
        question.ReviewedByName.ShouldBeNull();
        question.ReviewedAt.ShouldBeNull();
    }

    [Fact]
    public void Replacing_a_chapter_clears_the_reviewers_name_of_its_questions_and_keeps_the_others()
    {
        var version = DraftWithApprovedQuestion(out var changed, out var aboutChanged);
        var kept = ChapterId.New();
        version.AddChapter(TextChapter(kept, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        var aboutKept = version.AddQuestion(QuestionId.New(), MultipleChoice(kept), AnswerOptionId.New).Value;
        version.ApproveQuestion(aboutKept, Reviewer, ReviewersName, Later).IsSuccess.ShouldBeTrue();

        version.ReplaceChapter(TextChapter(changed, "Händehygiene, überarbeitet", "Neu."), Author).IsSuccess.ShouldBeTrue();

        var sentBack = version.Questions.Single(question => question.Id == aboutChanged);
        sentBack.Review.ShouldBe(ReviewState.Unreviewed);
        sentBack.ReviewedByName.ShouldBeNull();
        sentBack.ReviewedAt.ShouldBeNull();
        var untouched = version.Questions.Single(question => question.Id == aboutKept);
        untouched.Review.ShouldBe(ReviewState.Approved);
        untouched.ReviewedByName.ShouldBe(ReviewersName);
        untouched.ReviewedAt.ShouldBe(Later);
    }

    [Fact]
    public void The_next_version_copies_each_questions_reviewer_name_and_time_unchanged()
    {
        var published = InReviewWithASecondApprover(Approver);
        published.Publish(Reviewer, ReviewPolicy.FourEyes, Later).IsSuccess.ShouldBeTrue();

        var next = published.DraftNext(CourseVersionId.New(), Reviewer, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;

        published.Questions.Select(question => question.ReviewedByName).ShouldBe([AuthorsName, ApproversName]);
        next.Questions.Select(question => (question.ReviewedBy, question.ReviewedByName, question.ReviewedAt))
            .ShouldBe(published.Questions.Select(question => (question.ReviewedBy, question.ReviewedByName, question.ReviewedAt)));
    }

    [Fact]
    public void Applying_an_ai_test_draft_keeps_the_reviewers_name_of_an_approved_question()
    {
        var version = DraftWithQuestion(out var chapter, out var approved);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(3), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(approved, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();

        version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var kept = version.Questions.Single(question => question.Id == approved);
        kept.Review.ShouldBe(ReviewState.Approved);
        kept.ReviewedBy.ShouldBe(Author);
        kept.ReviewedByName.ShouldBe(AuthorsName);
        kept.ReviewedAt.ShouldBe(Now);
        version.Questions.Where(question => question.Id != approved).ShouldAllBe(question => question.ReviewedByName == null);
    }
}
