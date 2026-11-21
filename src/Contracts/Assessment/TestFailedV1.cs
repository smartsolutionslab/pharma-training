namespace SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

/// <summary>Assessment saw a person fail an attempt at the test of a course version; no consumer in MVP 1.</summary>
public sealed record TestFailedV1(Guid AttemptId, Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, int AttemptNumber) : IntegrationEvent;
