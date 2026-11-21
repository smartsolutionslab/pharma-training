namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record OrderDoesNotMatchChapters(CourseVersionId Version)
    : DomainError("editorial.order-does-not-match", "Die Reihenfolge muss jedes Kapitel genau einmal enthalten.");
