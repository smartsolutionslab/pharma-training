namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>Editorial retired a course version; consumed by Catalog and Compliance.</summary>
public sealed record CourseVersionRetiredV1(Guid CourseId, Guid CourseVersionId) : IntegrationEvent;
