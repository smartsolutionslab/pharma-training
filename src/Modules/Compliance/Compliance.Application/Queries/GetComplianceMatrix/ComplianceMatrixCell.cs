using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// A filled cell: the member's latest non-cancelled assignment of the course, its state today, and the German day the
/// course was last completed, if ever.
/// </summary>
public sealed record ComplianceMatrixCell(CourseId Course, AssignmentId Assignment, AssignmentState State, DueDate DueDate, CalendarDate? LastCompletedOn);
