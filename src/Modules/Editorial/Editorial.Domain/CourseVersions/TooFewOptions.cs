namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record TooFewOptions(QuestionId Question)
    : DomainError("editorial.too-few-options", "Eine Frage braucht mindestens zwei Antwortoptionen.");
