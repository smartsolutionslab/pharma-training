namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>Editorial published the test of a course version. Carries the solutions; consumed by Assessment only, never by Catalog or the frontend.</summary>
public sealed record TestDefinitionPublishedV1(Guid CourseId, Guid CourseVersionId, int PassingScorePercent, int MaxAttempts, IReadOnlyList<TestQuestionV1> Questions) : IntegrationEvent;
