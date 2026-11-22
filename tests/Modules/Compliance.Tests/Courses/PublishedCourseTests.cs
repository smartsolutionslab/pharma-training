using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;

public class PublishedCourseTests
{
    private readonly CourseId course = CourseId.From(Guid.CreateVersion7());

    [Fact]
    public void A_course_none_of_whose_versions_has_arrived_is_no_published_course() =>
        PublishedCourse.Of(course, [], []).HasValue.ShouldBeFalse();

    [Fact]
    public void A_course_with_only_a_retirement_and_no_version_is_no_published_course() =>
        PublishedCourse.Of(course, [], [Retirement(CourseVersionId.From(Guid.CreateVersion7()))]).HasValue.ShouldBeFalse();

    [Fact]
    public void A_course_with_one_version_has_it_as_its_latest_and_current_version()
    {
        var first = Version(1, "Kühlkette");

        var published = PublishedCourse.Of(course, [first], []).Value;

        published.Course.ShouldBe(course);
        published.Latest.ShouldBeSameAs(first);
        published.Title.ShouldBe(CourseTitle.From("Kühlkette"));
        published.State.ShouldBe(PublishedCourseState.Available);
        published.CurrentVersion().Value.ShouldBeSameAs(first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_current_version_is_the_highest_number_whatever_order_the_versions_arrived_in(bool newerFirst)
    {
        var older = Version(1, "Kühlkette 1");
        var newer = Version(2, "Kühlkette 2");
        IReadOnlyCollection<CourseVersion> arrived = newerFirst ? [newer, older] : [older, newer];

        var published = PublishedCourse.Of(course, arrived, []).Value;

        published.Latest.ShouldBeSameAs(newer);
        published.Title.ShouldBe(CourseTitle.From("Kühlkette 2"));
        published.State.ShouldBe(PublishedCourseState.Available);
        published.CurrentVersion().Value.ShouldBeSameAs(newer);
    }

    [Fact]
    public void A_course_whose_highest_version_is_retired_has_no_current_version_but_keeps_that_versions_title()
    {
        var older = Version(1, "Kühlkette 1");
        var newer = Version(2, "Kühlkette 2");

        var published = PublishedCourse.Of(course, [older, newer], [Retirement(newer.Id)]).Value;

        published.State.ShouldBe(PublishedCourseState.Retired);
        published.CurrentVersion().HasValue.ShouldBeFalse();
        published.Latest.ShouldBeSameAs(newer);
        published.Title.ShouldBe(CourseTitle.From("Kühlkette 2"));
    }

    [Fact]
    public void Retiring_a_lower_version_leaves_the_highest_current()
    {
        var older = Version(1, "Kühlkette 1");
        var newer = Version(2, "Kühlkette 2");

        var published = PublishedCourse.Of(course, [older, newer], [Retirement(older.Id)]).Value;

        published.State.ShouldBe(PublishedCourseState.Available);
        published.CurrentVersion().Value.ShouldBeSameAs(newer);
    }

    [Fact]
    public void A_retirement_of_a_version_that_has_not_arrived_changes_nothing()
    {
        var first = Version(1, "Kühlkette");

        var published = PublishedCourse.Of(course, [first], [Retirement(CourseVersionId.From(Guid.CreateVersion7()))]).Value;

        published.State.ShouldBe(PublishedCourseState.Available);
        published.CurrentVersion().Value.ShouldBeSameAs(first);
    }

    [Fact]
    public void A_version_of_another_course_is_refused()
    {
        var other = CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), CourseId.From(Guid.CreateVersion7()), VersionNumber.From(1), CourseTitle.From("Hygiene"), ValidityMonths.From(12));

        Should.Throw<GuardException>(() => PublishedCourse.Of(course, [Version(1, "Kühlkette"), other], []));
    }

    [Fact]
    public void A_retirement_of_another_course_is_refused()
    {
        var other = CourseVersionRetirement.Of(CourseVersionId.From(Guid.CreateVersion7()), CourseId.From(Guid.CreateVersion7()));

        Should.Throw<GuardException>(() => PublishedCourse.Of(course, [Version(1, "Kühlkette")], [other]));
    }

    private CourseVersion Version(int number, string title) =>
        CourseVersion.Published(CourseVersionId.From(Guid.CreateVersion7()), course, VersionNumber.From(number), CourseTitle.From(title), ValidityMonths.From(24));

    private CourseVersionRetirement Retirement(CourseVersionId version) => CourseVersionRetirement.Of(version, course);
}
