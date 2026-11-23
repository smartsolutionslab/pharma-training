using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>A column of the matrix: a course assigned in the pharmacy, under its latest title, with its own tally over every row.</summary>
public sealed record ComplianceMatrixCourse(CourseId Course, CourseTitle Title, AssignmentTally Tally);
