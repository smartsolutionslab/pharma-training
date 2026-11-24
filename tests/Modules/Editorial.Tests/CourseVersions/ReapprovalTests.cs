using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An approved question is not approved again (review 6013011898 on #151): a second approval is refused with its own
/// code, and the first approver and the time stay recorded. Only an edit takes the approval away, and then the question
/// is approved afresh.
/// </summary>
public class ReapprovalTests
{
    private const string QuestionAlreadyApproved = "editorial.question-already-approved";

    private static readonly Timestamp Later = Timestamp.From(Now.Value.AddHours(3));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Approving_an_approved_question_of_a_draft_again_is_refused_and_the_first_approval_stays(bool sameEditor)
    {
        var version = DraftWithApprovedQuestion(out _, out var id);

        var approved = version.ApproveQuestion(id, sameEditor ? Author : Reviewer, sameEditor ? AuthorsName : ReviewersName, Later);

        approved.IsFailure.ShouldBeTrue("an approved question was approved again");
        approved.Error.Code.ShouldBe(QuestionAlreadyApproved);
        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Approved);
        question.ReviewedBy.ShouldBe(Author);
        question.ReviewedAt.ShouldBe(Now);
    }

    [Fact]
    public void Approving_an_approved_question_of_a_version_in_review_again_is_refused_and_the_first_approval_stays()
    {
        var version = InReview();
        var question = version.Questions.ShouldHaveSingleItem();

        var approved = version.ApproveQuestion(question.Id, Reviewer, ReviewersName, Later);

        approved.IsFailure.ShouldBeTrue("an approved question of a version in review was approved again");
        approved.Error.Code.ShouldBe(QuestionAlreadyApproved);
        question.ReviewedBy.ShouldBe(Author);
        question.ReviewedAt.ShouldBe(Now);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
    }

    [Fact]
    public void An_approved_question_that_an_edit_sent_back_to_review_is_approved_again()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out var id);
        version.EditQuestion(id, MultipleChoice(chapter), Author, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var approved = version.ApproveQuestion(id, Reviewer, ReviewersName, Later);

        approved.IsSuccess.ShouldBeTrue();
        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Approved);
        question.ReviewedBy.ShouldBe(Reviewer);
        question.ReviewedAt.ShouldBe(Later);
    }
}
