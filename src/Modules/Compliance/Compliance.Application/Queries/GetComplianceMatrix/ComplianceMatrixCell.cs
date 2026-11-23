using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;

/// <summary>
/// A filled cell: the member's latest non-cancelled assignment of the course, its state today, the German day the course
/// was last completed, if ever, and, when the shown assignment is completed, the last day that completion stays valid:
/// its German day plus the months of the course's latest version (Compliance does not keep the certificate's own).
/// </summary>
public sealed record ComplianceMatrixCell(
    CourseId Course,
    AssignmentId Assignment,
    AssignmentState State,
    DueDate DueDate,
    CalendarDate? LastCompletedOn,
    CalendarDate? ValidUntil);
