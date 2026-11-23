namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>An active member of a pharmacy as the assign wizard lists it: who they are, their name and job function now.</summary>
public sealed record ComplianceMemberDto(Guid PersonId, string Name, ComplianceJobFunction JobFunction);
