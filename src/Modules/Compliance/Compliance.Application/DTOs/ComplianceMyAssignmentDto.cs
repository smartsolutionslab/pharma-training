namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>One of the caller's active assignments on their dashboard: the course, its state today, its due date and the
/// course version to open, if any, and whether that continues a session already running.</summary>
public sealed record ComplianceMyAssignmentDto(
    Guid AssignmentId,
    Guid CourseId,
    string CourseTitle,
    ComplianceAssignmentState State,
    DateOnly DueDate,
    Guid? StartCourseVersionId,
    bool ContinuesSession);
