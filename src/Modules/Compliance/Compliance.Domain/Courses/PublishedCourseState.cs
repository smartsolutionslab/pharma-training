namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>
/// Whether a course's latest version is still available or retired, which leaves the course without a current version.
/// A retirement of a version not received yet leaves the state as it is, as Catalog lists courses.
/// </summary>
public enum PublishedCourseState
{
    Available,
    Retired,
}
