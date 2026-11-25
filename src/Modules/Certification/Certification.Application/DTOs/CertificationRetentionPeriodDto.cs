namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

/// <summary>A pharmacy's retention period in years, and whether it is the default because the pharmacy chose none (#391).</summary>
public sealed record CertificationRetentionPeriodDto(int Years, bool IsDefault);
