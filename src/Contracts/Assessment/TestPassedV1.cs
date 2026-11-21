namespace SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

/// <summary>Assessment saw a person pass the test of a course version; consumed by Certification and Learning.</summary>
public sealed record TestPassedV1(Guid AttemptId, Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, int ScorePercent, DateTimeOffset PassedAt) : IntegrationEvent;
