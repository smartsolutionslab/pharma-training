namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record ChapterNotFound(CourseVersionId Version, ChapterId Chapter)
    : DomainError("editorial.chapter-not-found", "The course version has no such chapter.");
