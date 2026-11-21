namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>Where a course version is in its life, from the first draft to its retirement.</summary>
public enum CourseVersionStatus
{
    Draft,
    InReview,
    Published,
    Superseded,
    Retired,
}
