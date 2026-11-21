namespace SmartSolutionsLab.PharmaTraining.Contracts.Compliance;

/// <summary>Compliance assigned a course to a person in a pharmacy, due on a day; no consumer in MVP 1.</summary>
public sealed record CourseAssignedV1(Guid AssignmentId, Guid PersonId, Guid PharmacyId, Guid CourseId, DateOnly DueDate) : IntegrationEvent;
