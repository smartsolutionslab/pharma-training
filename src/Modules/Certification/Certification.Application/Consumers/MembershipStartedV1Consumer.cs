using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Stores a membership Organisation started, once, with the name a certificate's holder is given and the role it
/// started in. A membership starts
/// once, so a membership already stored means its event was handled, whatever the event's identifier or payload: a
/// second delivery changes nothing, and the first one wins. The job function is not kept, so not checked.
/// It deduplicates on the membership id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// A membership of a person Certification erased (#115) is dropped, logged without the name, so a redelivery or a replayed
/// dead letter cannot write the name back; the person is held before the tombstone is read, as the erasure holds them.
/// </summary>
public sealed partial class MembershipStartedV1Consumer(IMembershipStore memberships, IErasedPersonStore erased, ICertificationUnitOfWork unitOfWork, ILogger<MembershipStartedV1Consumer> logger)
{
    public async Task HandleAsync(MembershipStartedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        ConsumerGuards.IsText(message.DisplayName, nameof(message.DisplayName), HolderName.MaxLength);
        OrganisationRoles.Guard(message.Role, nameof(message.Role));

        var person = PersonId.From(message.PersonId);
        await erased.HoldAsync(person, cancellationToken);
        if (await erased.IsErasedAsync(person, cancellationToken))
        {
            ErasedPersonDropped(logger, message.MembershipId);
            return;
        }

        var membership = MembershipId.From(message.MembershipId);
        if (await memberships.ExistsAsync(membership, cancellationToken))
        {
            return;
        }

        memberships.Add(Membership.Started(
            membership,
            person,
            PharmacyId.From(message.PharmacyId),
            HolderName.From(message.DisplayName),
            OrganisationRoles.ToRole(message.Role),
            Timestamp.From(message.StartedAt)));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Membership {Membership} belongs to an erased person; its event is dropped.")]
    private static partial void ErasedPersonDropped(ILogger logger, Guid membership);
}
