namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NoChapters(CourseVersionId Version)
    : CourseVersionWorkflowError("editorial.no-chapters", "Eine Schulung braucht mindestens ein Kapitel.");
