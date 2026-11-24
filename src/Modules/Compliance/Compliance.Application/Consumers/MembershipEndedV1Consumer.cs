using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Stores that Organisation ended a membership, once, whether the membership's start has arrived yet or not: the member
/// is kept, and reads as ended whenever it is there. It deduplicates on the membership id, not on
/// <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish
/// under a new identifier.
/// <para>
/// Unless the person still has another membership in the pharmacy that has not ended (a re-invited person has a new
/// one), it then cancels their open and in-progress assignments there (#30): by the leaving person, the only person the
/// event names, for the reason <see cref="ChangeReason.MembershipEnded"/>, at Compliance's clock. Completed assignments
/// and those in other pharmacies are never loaded, so never touched. A redelivery sweeps again, so an assignment another
/// request committed while the first delivery ran is cancelled then; what is already cancelled is not loaded again.
/// It commits only when it stored or cancelled something. A save that lost a race on an assignment throws, and the
/// retry is idempotent. Nothing is announced to other modules.
/// </para>
/// </summary>
public sealed class MembershipEndedV1Consumer(IMemberStore members, IAssignmentStore assignments, IComplianceUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task HandleAsync(MembershipEndedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();

        var membership = MembershipId.From(message.MembershipId);
        var person = PersonId.From(message.PersonId);
        var pharmacy = PharmacyId.From(message.PharmacyId);

        var changed = false;
        if (!await members.IsEndedAsync(membership, cancellationToken))
        {
            members.Add(MembershipEnding.Of(membership, person, pharmacy));
            changed = true;
        }

        var others = (await members.ActiveMembershipsAsync(person, pharmacy, cancellationToken))
            .Where(active => active.Membership != membership);
        if (!others.Any())
        {
            var now = Timestamp.From(time.GetUtcNow());
            foreach (var assignment in await assignments.ActiveOfLearnerInPharmacyAsync(person, pharmacy, cancellationToken))
            {
                var cancelled = assignment.Cancel(person, ChangeReason.MembershipEnded, now);
                if (cancelled.IsFailure)
                {
                    throw new UnreachableException($"An active assignment {assignment.Id} was refused cancelling: {cancelled.Error}.");
                }

                changed = true;
            }
        }

        if (changed)
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
    }
}
