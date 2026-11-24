namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record QuestionNotFound(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-not-found", "The course version has no such question.");
