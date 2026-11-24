namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A row of the team matrix: an active member, their name and job function now, and their filled cells in column order.</summary>
public sealed record ComplianceMatrixRowDto(Guid PersonId, string Name, ComplianceJobFunction JobFunction, IReadOnlyList<ComplianceMatrixCellDto> Cells);
