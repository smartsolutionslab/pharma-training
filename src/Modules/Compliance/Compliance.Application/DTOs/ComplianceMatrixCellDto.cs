namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>
/// A filled cell of the team matrix: the assignment it shows, its state today, its due date, the day the course was
/// last completed, if ever, and, when the assignment shown is completed, the last day that completion stays valid; a
/// completed cell past that day is no longer compliant though its state stays completed.
/// </summary>
public sealed record ComplianceMatrixCellDto(
    Guid CourseId,
    Guid AssignmentId,
    ComplianceAssignmentState State,
    DateOnly DueDate,
    DateOnly? LastCompletedOn,
    DateOnly? ValidUntil);
