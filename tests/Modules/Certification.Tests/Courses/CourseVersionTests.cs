using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Courses;

/// <summary>The local copy of a published course version: its course, number, title and validity, and the factory that refuses a missing part.</summary>
public class CourseVersionTests
{
    private static readonly CourseVersionId Id = CourseVersionId.From(Guid.CreateVersion7());
    private static readonly CourseId Course = CourseId.From(Guid.CreateVersion7());
    private static readonly CourseTitle Title = CourseTitle.From("Hygiene 2027");
    private static readonly CourseVersionNumber Number = CourseVersionNumber.From(3);

    [Fact]
    public void A_published_version_keeps_its_course_number_title_and_validity()
    {
        var version = CourseVersion.Published(Id, Course, Number, Title, RecurrenceInterval.Yearly);

        version.Id.ShouldBe(Id);
        version.Course.ShouldBe(Course);
        version.Number.ShouldBe(Number);
        version.Title.ShouldBe(Title);
        version.Validity.ShouldBe(RecurrenceInterval.Yearly);
    }

    [Fact]
    public void A_published_version_without_any_of_its_parts_is_refused()
    {
        Should.Throw<GuardException>(() => CourseVersion.Published(null!, Course, Number, Title, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, null!, Number, Title, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, Course, null!, Title, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, Course, Number, null!, RecurrenceInterval.Yearly));
        Should.Throw<GuardException>(() => CourseVersion.Published(Id, Course, Number, Title, null!));
    }
}
