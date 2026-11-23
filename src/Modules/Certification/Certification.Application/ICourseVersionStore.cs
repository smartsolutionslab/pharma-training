using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// Certification's copies of Editorial's course versions; implemented in its Infrastructure project. What is added is
/// saved when the module's unit of work commits; the consumers never change what they stored.
/// </summary>
public interface ICourseVersionStore
{
    Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken);

    void Add(CourseVersion version);

    /// <summary>The version stored under that id; none while it has not arrived.</summary>
    Task<Option<CourseVersion>> FindAsync(CourseVersionId version, CancellationToken cancellationToken);
}
