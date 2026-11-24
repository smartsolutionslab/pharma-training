using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>A row of the matrix: an active member as they stand now, with a cell for each column they hold an assignment of.</summary>
public sealed record ComplianceMatrixRow(CurrentMembership Member, IReadOnlyList<ComplianceMatrixCell> Cells);
