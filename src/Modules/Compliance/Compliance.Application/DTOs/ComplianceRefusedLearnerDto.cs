namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A learner not assigned the course, with the refusal's code and message.</summary>
public sealed record ComplianceRefusedLearnerDto(Guid PersonId, string Code, string Message);
