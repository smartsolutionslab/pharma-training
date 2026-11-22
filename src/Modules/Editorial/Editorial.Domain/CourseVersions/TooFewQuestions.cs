namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A blueprint that asks for fewer questions than <see cref="TestBlueprint.MinimumQuestions"/>.</summary>
public sealed record TooFewQuestions(QuestionCount Requested)
    : DomainError("editorial.invalid-question-count", $"A test has {TestBlueprint.MinimumQuestions} to {QuestionCount.Maximum} questions.");
