namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotPublished(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-published", "The version is not published.");
