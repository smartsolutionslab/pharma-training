namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// The body of <c>PATCH /compliance/pharmacies/{id}/assignments/{assignmentId}</c>: the new due date and, if the manager
/// gives one, why. A blank reason is none.
/// </summary>
public sealed record ChangeComplianceAssignmentRequest(DateOnly? DueDate, string? Reason);
