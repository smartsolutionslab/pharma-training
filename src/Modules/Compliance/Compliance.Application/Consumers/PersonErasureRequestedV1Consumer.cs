using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Compliance;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Erases a person in Compliance (#115) and announces it as <see cref="ComplianceErasureCompletedV1"/>: it tombstones
/// the person (an unknown one too, so a membership that arrives late cannot write the name), forgets the name on every
/// member and change of theirs, cancels their open and in-progress assignments in every pharmacy as
/// <see cref="MembershipEndedV1Consumer"/> does (by the person, for <see cref="ChangeReason.MembershipEnded"/>, at
/// Compliance's clock), and commits that with the announcement in one commit. Before the commit it purges the stored
/// membership messages that hold their id; the purge runs on a connection of its own and commits by itself, so a
/// redelivery purges again. The person is held from the first read to the commit
/// (<see cref="IErasedPersonStore.HoldAsync"/>), as the membership consumers hold them, so neither can slip a name or a
/// tombstone between the other's read and commit.
/// <para>
/// It is idempotent by state, not by <c>Metadata.EventIdentifier</c>: a redelivery finds the tombstone, the names gone
/// and the assignments cancelled, changes nothing, and announces the completion again, which Organisation records once.
/// Two deliveries at once wait for each other under the hold, so the second finds the first's tombstone.
/// </para>
/// </summary>
public sealed class PersonErasureRequestedV1Consumer(
    IMemberStore members,
    IErasedPersonStore erased,
    IAssignmentStore assignments,
    IPersonalMessagePurge purge,
    IComplianceUnitOfWork unitOfWork,
    TimeProvider time)
{
    private readonly IMemberStore members = members;
    private readonly IErasedPersonStore erased = erased;
    private readonly IAssignmentStore assignments = assignments;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly IComplianceUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public async Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        var person = PersonId.From(message.PersonId);
        var now = Timestamp.From(time.GetUtcNow());

        // Held before anything of the person is read, so a membership event running at the same time either commits its
        // name before these reads (and it is erased here) or waits and then finds the tombstone.
        await erased.HoldAsync(person, cancellationToken);
        if (!await erased.IsErasedAsync(person, cancellationToken))
        {
            erased.Add(ErasedPerson.Of(person, now));
        }

        foreach (var member in await members.MembersOfPersonAsync(person, cancellationToken))
        {
            member.EraseName();
        }

        foreach (var change in await members.ChangesOfPersonAsync(person, cancellationToken))
        {
            change.EraseName();
        }

        foreach (var assignment in await assignments.ActiveOfLearnerInEveryPharmacyAsync(person, cancellationToken))
        {
            var cancelled = assignment.Cancel(person, ChangeReason.MembershipEnded, now);
            if (cancelled.IsFailure)
            {
                throw new UnreachableException($"An active assignment {assignment.Id} was refused cancelling: {cancelled.Error}.");
            }
        }

        await purge.PurgeAsync(person.Value, PersonalMessages.Types, cancellationToken);

        await unitOfWork.PublishAsync(new ComplianceErasureCompletedV1(message.ErasureId));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
