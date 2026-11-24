namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record NoChapters(CourseVersionId Version)
    : DomainError("editorial.no-chapters", "A course needs at least one chapter.");
