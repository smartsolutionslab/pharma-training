using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

/// <summary>
/// Certification's copy of a course version Editorial published: its course, number, title and how long a certificate for it
/// stays valid. Editorial owns it; Certification stores it once, as it first arrived, and never changes it.
/// </summary>
public sealed class CourseVersion : Entity<CourseVersionId>
{
    private CourseVersion(CourseVersionId id, CourseId course, CourseVersionNumber number, CourseTitle title, RecurrenceInterval validity)
        : base(id)
    {
        Course = course;
        Number = number;
        Title = title;
        Validity = validity;
    }

    public CourseId Course { get; }

    public CourseVersionNumber Number { get; }

    public CourseTitle Title { get; }

    public RecurrenceInterval Validity { get; }

    public static CourseVersion Published(CourseVersionId id, CourseId course, CourseVersionNumber number, CourseTitle title, RecurrenceInterval validity)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(number).IsNotNull();
        Ensure.That(title).IsNotNull();
        Ensure.That(validity).IsNotNull();
        return new CourseVersion(id, course, number, title, validity);
    }
}
