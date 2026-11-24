namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>
/// The team matrix: every course with a non-cancelled assignment in the pharmacy as a column, a page of its active
/// members as rows, and the counted, compliant and overdue assignments with the compliance rate over every row of the
/// filter, not only this page.
/// </summary>
public sealed record ComplianceMatrixDto(
    IReadOnlyList<ComplianceMatrixCourseDto> Courses,
    PagedResult<ComplianceMatrixRowDto> Members,
    int Assignments,
    int CompliantAssignments,
    int OverdueAssignments,
    double? ComplianceRate);
