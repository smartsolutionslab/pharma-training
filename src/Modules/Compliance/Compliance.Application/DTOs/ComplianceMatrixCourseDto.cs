namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>
/// A column of the team matrix: the course, its title, and over every row its counted, compliant and overdue
/// assignments and its compliance rate.
/// </summary>
public sealed record ComplianceMatrixCourseDto(
    Guid CourseId,
    string Title,
    int Assignments,
    int CompliantAssignments,
    int OverdueAssignments,
    double? ComplianceRate);
