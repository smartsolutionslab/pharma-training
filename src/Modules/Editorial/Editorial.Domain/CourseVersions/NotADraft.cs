namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotADraft(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-a-draft", "Nur ein Entwurf kann bearbeitet werden.");
