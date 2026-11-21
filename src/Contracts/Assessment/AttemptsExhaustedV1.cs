namespace SmartSolutionsLab.PharmaTraining.Contracts.Assessment;

/// <summary>Assessment saw a person use up the attempts at the test of a course version, naming the chapters failed; consumed by Learning and Compliance.</summary>
public sealed record AttemptsExhaustedV1(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId, IReadOnlyList<Guid> FailedChapterIds) : IntegrationEvent;
