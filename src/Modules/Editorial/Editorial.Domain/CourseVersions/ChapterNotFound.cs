namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record ChapterNotFound(CourseVersionId Version, ChapterId Chapter)
    : DomainError("editorial.chapter-not-found", "Dieses Kapitel gibt es in der Version nicht.");
