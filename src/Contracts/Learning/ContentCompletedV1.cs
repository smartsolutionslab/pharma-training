namespace SmartSolutionsLab.PharmaTraining.Contracts.Learning;

/// <summary>Learning saw a person complete the content of a course version; consumed by Assessment.</summary>
public sealed record ContentCompletedV1(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId) : IntegrationEvent;
