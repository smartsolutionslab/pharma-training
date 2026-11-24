namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record OrderDoesNotMatchChapters(CourseVersionId Version)
    : DomainError("editorial.order-does-not-match", "The order must name every chapter exactly once.");
