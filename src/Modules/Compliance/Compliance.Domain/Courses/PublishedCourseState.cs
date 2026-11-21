namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>Whether a course's latest version is still available or retired, which leaves the course without a current version.</summary>
public enum PublishedCourseState
{
    Available,
    Retired,
}
