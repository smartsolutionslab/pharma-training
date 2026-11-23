using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>
/// The factories of the assignment's errors. The steps' errors return the union; AlreadyActive returns DomainError, since
/// no aggregate method returns it: the Application checks it across aggregates.
/// </summary>
public static class AssignmentErrors
{
    public static AssignmentError DueDateInThePast(DueDate dueDate) => new DueDateInThePast(dueDate);

    public static AssignmentError NotActive(AssignmentId assignment, AssignmentStatus status) => new AssignmentNotActive(assignment, status);

    public static AssignmentError CertificatePredatesAssignment(AssignmentId assignment, CertificateId certificate) =>
        new CertificatePredatesAssignment(assignment, certificate);

    public static DomainError AlreadyActive(PersonId learner, CourseId course) => new AssignmentAlreadyActive(learner, course);
}
