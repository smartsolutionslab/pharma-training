using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Retiring records who retired the version and when (ADR 0007, Consequences). It stays a one-person step, also under
/// four eyes: whoever submitted, approved or published the version may retire it alone.
/// </summary>
public class RetireRecordsTests
{
    private static readonly Timestamp Later = Timestamp.From(Now.Value.AddDays(30));

    [Fact]
    public void Retiring_records_who_retired_and_when()
    {
        var version = Published();

        version.Retire(Approver, Later).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.Retired);
        version.RetiredBy.ShouldBe(Approver);
        version.RetiredAt.ShouldBe(Later);
        version.DomainEvents.OfType<CourseVersionRetired>().ShouldHaveSingleItem().ShouldBe(new CourseVersionRetired(version.Id, version.Course, Approver));
    }

    [Fact]
    public void A_superseded_version_records_who_retired_it_too()
    {
        var version = Published();
        version.Supersede();

        version.Retire(Reviewer, Later).IsSuccess.ShouldBeTrue();

        version.RetiredBy.ShouldBe(Reviewer);
        version.RetiredAt.ShouldBe(Later);
    }

    [Fact]
    public void A_version_never_retired_names_no_one_who_retired_it()
    {
        var version = Published();

        version.RetiredBy.ShouldBeNull();
        version.RetiredAt.ShouldBeNull();
    }

    [Fact]
    public void A_refused_retirement_records_no_one()
    {
        var version = InReview();

        var retired = version.Retire(Reviewer, Later);

        retired.IsFailure.ShouldBeTrue();
        CourseVersionFailures.From(retired.Error).Code.ShouldBe("editorial.not-published");
        version.RetiredBy.ShouldBeNull();
        version.RetiredAt.ShouldBeNull();
    }

    /// <summary>A second retirement is refused, and the first one stays recorded.</summary>
    [Fact]
    public void Retiring_again_is_refused_and_the_first_retirement_stays()
    {
        var version = Published();
        version.Retire(Reviewer, Now).IsSuccess.ShouldBeTrue();

        var again = version.Retire(Approver, Later);

        again.IsFailure.ShouldBeTrue();
        version.RetiredBy.ShouldBe(Reviewer);
        version.RetiredAt.ShouldBe(Now);
    }

    /// <summary>
    /// The version was submitted and its question approved by the author and published by the reviewer under four eyes;
    /// each of them retires it alone.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Under_four_eyes_the_submitter_or_the_publisher_retires_alone(bool byPublisher)
    {
        var version = Published();
        var editor = byPublisher ? Reviewer : Author;

        version.Retire(editor, Later).IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CourseVersionStatus.Retired);
        version.RetiredBy.ShouldBe(editor);
    }
}
