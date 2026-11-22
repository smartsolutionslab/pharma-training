using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>
/// That Editorial retired a course version. Kept apart from the <see cref="CourseVersion"/>, so it is stored even when
/// it arrives before the version it retires.
/// </summary>
public sealed class CourseVersionRetirement : Entity<CourseVersionId>
{
    private CourseVersionRetirement(CourseVersionId id, CourseId course)
        : base(id) => Course = course;

    public CourseId Course { get; }

    public static CourseVersionRetirement Of(CourseVersionId id, CourseId course)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(course).IsNotNull();
        return new CourseVersionRetirement(id, course);
    }
}
