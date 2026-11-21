using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>
/// Compliance's copy of a course version Editorial published: its course, number, title and validity. Editorial owns
/// it; Compliance stores it once, as it first arrived, and never changes it. Whether it is retired is a
/// <see cref="CourseVersionRetirement"/> of its own.
/// </summary>
public sealed class CourseVersion : Entity<CourseVersionId>
{
    private CourseVersion(CourseVersionId id, CourseId course, VersionNumber number, CourseTitle title, ValidityMonths validity)
        : base(id)
    {
        Course = course;
        Number = number;
        Title = title;
        Validity = validity;
    }

    public CourseId Course { get; }

    public VersionNumber Number { get; }

    public CourseTitle Title { get; }

    public ValidityMonths Validity { get; }

    public static CourseVersion Published(CourseVersionId id, CourseId course, VersionNumber number, CourseTitle title, ValidityMonths validity)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(number).IsNotNull();
        Ensure.That(title).IsNotNull();
        Ensure.That(validity).IsNotNull();
        return new CourseVersion(id, course, number, title, validity);
    }
}
