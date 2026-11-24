namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NotADraft(CourseVersionId Version, CourseVersionStatus Status)
    : DomainError("editorial.not-a-draft", "Only a draft can be changed.");
