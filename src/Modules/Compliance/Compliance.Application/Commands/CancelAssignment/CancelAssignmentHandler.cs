using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.CancelAssignment;

/// <summary>
/// Withdraws an open or in-progress assignment once the caller may assign courses in the pharmacy (docs/adr/0003): it is
/// cancelled with a history entry by the caller and the fixed reason <see cref="ChangeReason.Withdrawn"/>. Withdrawing a
/// cancelled one again changes nothing; a completed one is refused (409). A save that lost a race saves nothing and is
/// told so (docs/adr/0002). Nothing is announced to other modules.
/// </summary>
public sealed class CancelAssignmentHandler(
    IMemberStore members,
    IAssignmentStore assignments,
    IComplianceUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<CancelAssignment, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(CancelAssignment command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var access = await ComplianceAccess.RequireAsync(members, command.By, command.Pharmacy, Capability.AssignCourses, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
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

        var cancelled = found.Value.Cancel(command.By, ChangeReason.Withdrawn, Timestamp.From(time.GetUtcNow()));
        if (cancelled.IsFailure)
        {
            return Failure(AssignmentFailures.From(cancelled.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(AssignmentFailures.LostRace(saved.Error));
        }

        return Success(Unit.Value);
    }
}
