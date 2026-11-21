using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// Compliance's copies of Editorial's course versions and their retirements; implemented in its Infrastructure project.
/// What is added is saved when the module's unit of work commits; the consumers never change what they stored.
/// </summary>
public interface ICourseVersionStore
{
    Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken);

    void Add(CourseVersion version);

    Task<bool> IsRetiredAsync(CourseVersionId version, CancellationToken cancellationToken);

    void Add(CourseVersionRetirement retirement);

    /// <summary>The course from the versions and retirements stored for it, for a read; none while no version has arrived.</summary>
    Task<Option<PublishedCourse>> FindCourseAsync(CourseId course, CancellationToken cancellationToken);
}
