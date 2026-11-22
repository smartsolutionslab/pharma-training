namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotInReview(CourseVersionId Version, CourseVersionStatus Status)
    : CourseVersionWorkflowError("editorial.not-in-review", "Die Version ist nicht in Prüfung.");
