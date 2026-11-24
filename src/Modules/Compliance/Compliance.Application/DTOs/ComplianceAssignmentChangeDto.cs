namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>
/// One entry of an assignment's history: what was done, by whom (a person id) and when, the due dates before and after a
/// change of it, and why.
/// </summary>
public sealed record ComplianceAssignmentChangeDto(
    ComplianceAssignmentChangeKind Kind,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    DateOnly? PreviousDueDate,
    DateOnly? NewDueDate,
    string? Reason,
    bool BySelf);
