using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Returning a version in review to draft hands back a <see cref="CourseVersionReturn"/>, the record of who returned it,
/// under which name (#220), when and why; the version keeps the latest editor and time, and raises <see cref="CourseVersionReturned"/> with the
/// reason (#181). A later return yields a record of its own and changes none before it.
/// </summary>
public class ReturnRecordsTests
{
    private static readonly Timestamp Later = Timestamp.From(Now.Value.AddHours(2));
    private static readonly ReturnReason Reason = ReturnReason.From("Kapitel 2 braucht eine Quelle.");
    private static readonly ReturnReason OtherReason = ReturnReason.From("Frage 1 ist mehrdeutig.");

    [Fact]
    public void Returning_records_who_when_and_why_and_raises_returned()
    {
        var version = InReview();

        var returned = version.ReturnToDraft(Reviewer, ReviewersName, Reason, Later);

        returned.IsSuccess.ShouldBeTrue();
        returned.Value.Version.ShouldBe(version.Id);
        returned.Value.ReturnedBy.ShouldBe(Reviewer);
        returned.Value.ReturnedByName.ShouldBe(ReviewersName);
        returned.Value.ReturnedAt.ShouldBe(Later);
        returned.Value.Reason.ShouldBe(Reason);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        version.ReturnedBy.ShouldBe(Reviewer);
        version.ReturnedAt.ShouldBe(Later);
        version.DomainEvents.OfType<CourseVersionReturned>().ShouldHaveSingleItem().ShouldBe(new CourseVersionReturned(version.Id, Reviewer, Reason));
    }

    [Fact]
    public void A_version_never_returned_names_no_one_who_returned_it()
    {
        var version = InReview();

        version.ReturnedBy.ShouldBeNull();
        version.ReturnedAt.ShouldBeNull();
        version.DomainEvents.OfType<CourseVersionReturned>().ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_return_yields_no_record_records_no_one_and_raises_nothing()
    {
        var version = DraftWithChapter(out _);

        var returned = version.ReturnToDraft(Reviewer, ReviewersName, Reason, Later);

        returned.IsFailure.ShouldBeTrue();
        CourseVersionFailures.From(returned.Error).Code.ShouldBe("editorial.not-in-review");
        version.ReturnedBy.ShouldBeNull();
        version.ReturnedAt.ShouldBeNull();
        version.DomainEvents.OfType<CourseVersionReturned>().ShouldBeEmpty();
    }

    /// <summary>The submitter may take their own version back: returning is not bound by four eyes.</summary>
    [Fact]
    public void The_submitter_may_return_the_version()
    {
        var version = InReview();

        version.ReturnToDraft(Author, AuthorsName, Reason, Later).IsSuccess.ShouldBeTrue();

        version.ReturnedBy.ShouldBe(Author);
    }

    /// <summary>A version returned, resubmitted and returned again yields a second record; the first keeps who, when and why.</summary>
    [Fact]
    public void A_second_return_yields_a_second_record_and_leaves_the_first_as_it_was()
    {
        var version = InReview();
        var first = version.ReturnToDraft(Reviewer, ReviewersName, Reason, Now).Value;
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var second = version.ReturnToDraft(Approver, ApproversName, OtherReason, Later).Value;

        second.Id.ShouldNotBe(first.Id);
        first.ReturnedBy.ShouldBe(Reviewer);
        first.ReturnedByName.ShouldBe(ReviewersName);
        first.Reason.ShouldBe(Reason);
        first.ReturnedAt.ShouldBe(Now);
        second.ReturnedBy.ShouldBe(Approver);
        second.ReturnedByName.ShouldBe(ApproversName);
        second.Reason.ShouldBe(OtherReason);
        second.ReturnedAt.ShouldBe(Later);
        version.ReturnedBy.ShouldBe(Approver);
        version.ReturnedAt.ShouldBe(Later);
        ReturnReason[] reasons = [Reason, OtherReason];
        version.DomainEvents.OfType<CourseVersionReturned>().Select(returned => returned.Reason).ShouldBe(reasons);
    }
}
