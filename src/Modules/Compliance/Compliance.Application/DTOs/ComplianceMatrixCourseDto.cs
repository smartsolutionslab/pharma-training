namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>A column of the team matrix: the course, its title, and its own active and overdue assignments and rate over every row.</summary>
public sealed record ComplianceMatrixCourseDto(Guid CourseId, string Title, int ActiveAssignments, int OverdueAssignments, double? ComplianceRate);
