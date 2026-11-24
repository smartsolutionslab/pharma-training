namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotInReview(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-in-review", "The version is not in review.");
