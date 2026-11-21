namespace SmartSolutionsLab.PharmaTraining.Contracts.Learning;

/// <summary>Learning started a person's session on a course version; consumed by Compliance.</summary>
public sealed record LearningSessionStartedV1(Guid SessionId, Guid PersonId, Guid CourseId, Guid CourseVersionId) : IntegrationEvent;
