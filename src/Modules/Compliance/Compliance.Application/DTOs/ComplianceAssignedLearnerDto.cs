namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A learner assigned the course, and the assignment that was created.</summary>
public sealed record ComplianceAssignedLearnerDto(Guid PersonId, Guid AssignmentId);
