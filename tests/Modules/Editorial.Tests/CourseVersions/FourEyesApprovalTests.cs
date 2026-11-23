using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Which approvals count for four eyes (ADR 0007, the owner's decision of 2026-11-22): a question copied by
/// <see cref="CourseVersion.DraftNext"/> keeps its approver, who then cannot publish the next version; an approval an
/// edit or a change of the test's settings took away no longer bars the editor who gave it. Refusals are read through <see cref="CourseVersionFailures.From(DomainError)"/>
/// so the tests hold whichever error type the workflow returns.
/// </summary>
public class FourEyesApprovalTests
{
    private const string PublisherApprovedQuestions = "editorial.publisher-approved-questions";

    [Fact]
    public void With_four_eyes_on_the_approver_of_a_copied_question_cannot_publish_the_next_version()
    {
        var next = NextOf(PublishedApprovedBy(Approver));
        next.Questions.ShouldHaveSingleItem().ReviewedBy.ShouldBe(Approver);
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var published = next.Publish(Approver, ReviewPolicy.FourEyes, Now);

        published.IsFailure.ShouldBeTrue("the approver of a copied question published the next version under four eyes");
        CourseVersionFailures.From(published.Error).Code.ShouldBe(PublisherApprovedQuestions);
        next.Status.ShouldBe(CourseVersionStatus.InReview);
        next.PublishedBy.ShouldBeNull();
    }

    [Fact]
    public void With_four_eyes_off_the_approver_of_a_copied_question_publishes_the_next_version()
    {
        var next = NextOf(PublishedApprovedBy(Approver));
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var published = next.Publish(Approver, ReviewPolicy.SingleEditor, Now);

        published.IsSuccess.ShouldBeTrue();
        next.PublishedBy.ShouldBe(Approver);
    }

    [Fact]
    public void With_four_eyes_on_an_editor_whose_approval_an_edit_took_away_publishes()
    {
        var version = DraftWithQuestion(out var chapter, out var id);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(id, Approver, ApproversName, Now).IsSuccess.ShouldBeTrue();
        version.EditQuestion(id, MultipleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(id, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var published = version.Publish(Approver, ReviewPolicy.FourEyes, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Approver);
    }

    [Fact]
    public void With_four_eyes_on_the_approver_of_a_copied_question_that_was_edited_and_approved_again_publishes_the_next_version()
    {
        var next = NextOf(PublishedApprovedBy(Approver));
        var copied = next.Questions.ShouldHaveSingleItem();
        next.EditQuestion(copied.Id, MultipleChoice(copied.Chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        next.ApproveQuestion(copied.Id, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var published = next.Publish(Approver, ReviewPolicy.FourEyes, Now);

        published.IsSuccess.ShouldBeTrue();
        next.PublishedBy.ShouldBe(Approver);
    }

    [Fact]
    public void With_four_eyes_on_the_approver_of_a_copied_question_whose_approval_a_blueprint_change_took_away_publishes_the_next_version()
    {
        var next = NextOf(PublishedApprovedBy(Approver));
        var copied = next.Questions.ShouldHaveSingleItem();
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(81), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        next.ApproveQuestion(copied.Id, Author, Now).IsSuccess.ShouldBeTrue();
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var published = next.Publish(Approver, ReviewPolicy.FourEyes, Now);

        published.IsSuccess.ShouldBeTrue();
        next.PublishedBy.ShouldBe(Approver);
    }

    /// <summary>A first version of one question approved by <paramref name="approver"/>, submitted by the author, published by the reviewer.</summary>
    private static CourseVersion PublishedApprovedBy(PersonId approver)
    {
        var version = DraftWithQuestion(out _, out var id);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(id, approver, ApproversName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static CourseVersion NextOf(CourseVersion published) =>
        published.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
}
