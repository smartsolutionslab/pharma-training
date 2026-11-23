namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>What assigning a course did: each learner assigned with the assignment's id, and each refused with the reason.</summary>
public sealed record ComplianceCourseAssignmentDto(IReadOnlyList<ComplianceAssignedLearnerDto> Assigned, IReadOnlyList<ComplianceRefusedLearnerDto> Refused);
