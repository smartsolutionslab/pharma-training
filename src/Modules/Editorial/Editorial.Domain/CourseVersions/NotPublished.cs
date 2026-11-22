namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotPublished(CourseVersionId Version, CourseVersionStatus Status)
    : CourseVersionWorkflowError("editorial.not-published", "Die Version ist nicht veröffentlicht.");
