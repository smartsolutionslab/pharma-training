namespace SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

/// <summary>Editorial retired a course version; consumed by Catalog and Compliance. May be republished under a new event identifier (docs/adr/0019): deduplicate on <c>CourseVersionId</c>.</summary>
public sealed record CourseVersionRetiredV1(Guid CourseId, Guid CourseVersionId) : IntegrationEvent;
