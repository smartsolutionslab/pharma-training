namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record QuestionNotFound(CourseVersionId Version, QuestionId Question)
    : DomainError("editorial.question-not-found", "Diese Frage gibt es in der Version nicht.");
