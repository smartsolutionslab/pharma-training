namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

public sealed record TooFewOptions(QuestionId Question)
    : DomainError("editorial.too-few-options", "A question needs at least two answer options.");
