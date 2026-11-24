namespace SmartSolutionsLab.PharmaTraining.Contracts.Compliance;

/// <summary>
/// Compliance completed a recurring assignment and its follow-up is due. Compliance's own work item: its own consumer
/// creates the follow-up. It never leaves the Compliance service (a module of the host consumes it, so Wolverine routes
/// it to the host's durable local queue and never to the broker), so no other module can consume it; other modules
/// learn of the follow-up from CourseAssignedV1. It carries the completed assignment's id only (docs/adr/0026).
/// </summary>
public sealed record AssignmentFollowUpRequestedV1(Guid AssignmentId) : IntegrationEvent;
