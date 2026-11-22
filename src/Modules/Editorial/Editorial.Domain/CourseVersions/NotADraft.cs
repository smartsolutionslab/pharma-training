namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotADraft(CourseVersionId Version, CourseVersionStatus Status)
    : CourseVersionWorkflowError("editorial.not-a-draft", "Nur ein Entwurf kann bearbeitet werden.");
