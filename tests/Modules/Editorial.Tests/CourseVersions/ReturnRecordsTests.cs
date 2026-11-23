using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Returning a version in review to draft records who returned it and when, and raises <see cref="CourseVersionReturned"/>
/// with the version and that person (ADR 0007, Consequences).
/// </summary>
public class ReturnRecordsTests
{
    private static readonly Timestamp Later = Timestamp.From(Now.Value.AddHours(2));

    [Fact]
    public void Returning_records_who_returned_and_when_and_raises_returned()
    {
        var version = InReview();

        version.ReturnToDraft(Reviewer, Later).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.Draft);
        version.ReturnedBy.ShouldBe(Reviewer);
        version.ReturnedAt.ShouldBe(Later);
        version.DomainEvents.OfType<CourseVersionReturned>().ShouldHaveSingleItem().ShouldBe(new CourseVersionReturned(version.Id, Reviewer));
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
    public void A_refused_return_records_no_one_and_raises_nothing()
    {
        var version = DraftWithChapter(out _);

        var returned = version.ReturnToDraft(Reviewer, Later);

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

        version.ReturnToDraft(Author, Later).IsSuccess.ShouldBeTrue();

        version.ReturnedBy.ShouldBe(Author);
    }

    /// <summary>A version returned, resubmitted and returned again names the latest editor who returned it, and raises an event each time.</summary>
    [Fact]
    public void A_second_return_records_the_latest_editor()
    {
        var version = InReview();
        version.ReturnToDraft(Reviewer, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        version.ReturnToDraft(Approver, Later).IsSuccess.ShouldBeTrue();

        version.ReturnedBy.ShouldBe(Approver);
        version.ReturnedAt.ShouldBe(Later);
        PersonId[] returners = [Reviewer, Approver];
        version.DomainEvents.OfType<CourseVersionReturned>().Select(returned => returned.ReturnedBy).ShouldBe(returners);
    }
}
