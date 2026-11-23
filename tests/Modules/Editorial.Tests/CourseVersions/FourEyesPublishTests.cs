using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The owner's decision of 2026-11-22: with four eyes on, the editor who publishes a version is neither the one who
/// submitted it nor anyone who approved one of its questions. The submitter may approve questions before submitting;
/// only the publisher is constrained. A publisher who is the submitter is refused as the submitter, whether or not
/// they also approved. With four eyes off nothing changes.
/// </summary>
public class FourEyesPublishTests
{
    private const string PublisherApprovedQuestions = "editorial.publisher-approved-questions";
    private const string FourEyes = "editorial.four-eyes";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_four_eyes_on_an_editor_who_approved_a_question_cannot_publish(bool approvedWhileInReview)
    {
        var version = InReviewWithASecondApprover(Approver, approvedWhileInReview);

        var published = version.Publish(Approver, ReviewPolicy.FourEyes, Now);

        published.IsFailure.ShouldBeTrue("an editor who approved one of the version's questions published it under four eyes");
        CourseVersionFailures.From(published.Error).Code.ShouldBe(PublisherApprovedQuestions);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.PublishedBy.ShouldBeNull();
        version.DomainEvents.OfType<CourseVersionPublished>().ShouldBeEmpty();
    }

    [Fact]
    public void With_four_eyes_on_the_submitter_who_approved_nothing_is_refused_as_the_submitter()
    {
        var version = InReviewWithASecondApprover(Approver);

        var published = version.Publish(Author, ReviewPolicy.FourEyes, Now);

        published.Error.Value.ShouldBeOfType<FourEyesRequired>();
        version.Status.ShouldBe(CourseVersionStatus.InReview);
    }

    /// <summary>Both rules apply to the author, who submitted and approved: the submitter's code wins.</summary>
    [Fact]
    public void With_four_eyes_on_a_submitter_who_also_approved_is_refused_as_the_submitter()
    {
        var version = InReview();

        var published = version.Publish(Author, ReviewPolicy.FourEyes, Now);

        published.Error.Value.ShouldBeOfType<FourEyesRequired>().Code.ShouldBe(FourEyes);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
    }

    [Fact]
    public void With_four_eyes_on_an_editor_who_neither_submitted_nor_approved_publishes()
    {
        var version = InReviewWithASecondApprover(Approver);

        var published = version.Publish(Reviewer, ReviewPolicy.FourEyes, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
    }

    [Fact]
    public void With_four_eyes_off_an_editor_who_approved_a_question_publishes()
    {
        var version = InReviewWithASecondApprover(Approver);

        var published = version.Publish(Approver, ReviewPolicy.SingleEditor, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Approver);
    }

    /// <summary>The submitter approving before submitting constrains nobody but the publisher.</summary>
    [Fact]
    public void With_four_eyes_on_the_submitter_may_approve_before_submitting_and_another_editor_publishes()
    {
        var version = InReview();
        version.Questions.ShouldAllBe(question => question.ReviewedBy == Author);
        version.SubmittedBy.ShouldBe(Author);

        var published = version.Publish(Reviewer, ReviewPolicy.FourEyes, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
    }
}
