namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>
/// A filled cell of the team matrix: the assignment it shows, its state today, its due date and the day the course was
/// last completed, if ever.
/// </summary>
public sealed record ComplianceMatrixCellDto(Guid CourseId, Guid AssignmentId, ComplianceAssignmentState State, DateOnly DueDate, DateOnly? LastCompletedOn);
