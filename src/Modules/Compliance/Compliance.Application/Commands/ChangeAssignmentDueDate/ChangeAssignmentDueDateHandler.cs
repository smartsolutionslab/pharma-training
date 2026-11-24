using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.ChangeAssignmentDueDate;

/// <summary>
/// Changes an assignment's due date once the caller may assign courses in the pharmacy (docs/adr/0003). The new day
/// obeys the rules of assigning: not before today in Germany (422, the assignment's own check) and not more than
/// <see cref="AssignCourse.AssignCourse.MaximumYearsAhead"/> years ahead (400). The assignment keeps the change in its history; the
/// same day again changes nothing. A save that lost a race saves nothing and is told so (docs/adr/0002). Nothing is
/// announced to other modules.
/// </summary>
public sealed class ChangeAssignmentDueDateHandler(
    IMemberStore members,
    IAssignmentStore assignments,
    IComplianceUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<ChangeAssignmentDueDate, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ChangeAssignmentDueDate command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var access = await ComplianceAccess.RequireAsync(members, command.By, command.Pharmacy, Capability.AssignCourses, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var now = Timestamp.From(time.GetUtcNow());
        if (command.DueDate.Value > now.DayInGermany().Value.AddYears(AssignCourse.AssignCourse.MaximumYearsAhead))
        {
            return Failure(AssignmentFailures.DueDateTooFar());
        }

        var found = await assignments.FindInPharmacyAsync(command.Pharmacy, command.Assignment, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(AssignmentFailures.AssignmentNotFound(command.Assignment.Value));
        }

        // Nobody manages their own assignment: another manager of the pharmacy moves or withdraws it.
        if (found.Value.Learner == command.By)
        {
            return Failure(AssignmentFailures.OwnAssignment());
        }

        var changed = found.Value.ChangeDueDate(command.DueDate, command.By, command.Reason, now);
        if (changed.IsFailure)
        {
            return Failure(AssignmentFailures.From(changed.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(AssignmentFailures.LostRace(saved.Error));
        }

        return Success(Unit.Value);
    }
}
