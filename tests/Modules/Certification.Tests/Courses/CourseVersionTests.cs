using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Courses;

/// <summary>The local copy of a published course version: what it keeps, and the factory that refuses a missing part.</summary>
public class CourseVersionTests
{
    private static readonly CourseVersionId Id = CourseVersionId.From(Guid.CreateVersion7());
    private static readonly CourseId Course = CourseId.From(Guid.CreateVersion7());
    private static readonly CourseTitle Title = CourseTitle.From("Hygiene 2027");

    [Fact]
    public void A_published_version_keeps_its_course_title_and_validity()
    {
        var version = CourseVersion.Published(Id, Course, Title, RecurrenceInterval.Yearly);

        version.Id.ShouldBe(Id);
        version.Course.ShouldBe(Course);
        version.Title.ShouldBe(Title);
        version.Validity.ShouldBe(RecurrenceInterval.Yearly);
    }

    [Fact]
    public void A_published_version_without_any_of_its_parts_is_refused()
    {
        Should.Throw<GuardException>(() => CourseVersion.Published(null!, Course, Title, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, null!, Title, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, Course, null!, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, Course, Title, null!));
    }
}
