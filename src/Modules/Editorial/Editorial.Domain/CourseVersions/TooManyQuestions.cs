namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A question, or an AI draft, that would take the version beyond <see cref="QuestionCount.Maximum"/> questions.</summary>
public sealed record TooManyQuestions(CourseVersionId Version)
    : DomainError("editorial.too-many-questions", $"Ein Test hat höchstens {QuestionCount.Maximum} Fragen.");
