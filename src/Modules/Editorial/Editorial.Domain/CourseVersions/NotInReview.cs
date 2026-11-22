namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotInReview(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-in-review", "Die Version ist nicht in Prüfung.");
