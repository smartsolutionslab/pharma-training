using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Stores a change Organisation made to a membership's role, once per version, beside the membership and whether or not
/// it has arrived yet; which role is current is decided when it is read (docs/adr/0015). A change already stored at that
/// version means its event was handled, so a second delivery changes nothing and the first one wins. Only the role is
/// kept; the name and the job function are not, so not checked. It deduplicates on the membership and the version, not
/// on <c>Metadata.EventIdentifier</c>, for the reason <see cref="MembershipStartedV1Consumer"/> gives. A change of an
/// erased person is dropped under the same hold (#115).
/// </summary>
/// <remarks>
/// The handler takes the message and a cancellation token only; everything else comes by constructor, since the
/// AppHost's messaging topology reads every parameter type of a consumer's handler (docs/adr/0011).
/// </remarks>
public sealed partial class MembershipChangedV1Consumer(IMembershipStore memberships, IErasedPersonStore erased, ICertificationUnitOfWork unitOfWork, ILogger<MembershipChangedV1Consumer> logger)
{
    public async Task HandleAsync(MembershipChangedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        OrganisationRoles.Guard(message.Role, nameof(message.Role));
        Ensure.That(message.Version, nameof(message.Version)).AtLeast(2);

        // Held and checked as MembershipStartedV1Consumer does (#115): an erased person's change is dropped, and an
        // erasure running at the same time either commits first (and this finds the tombstone) or waits.
        var person = PersonId.From(message.PersonId);
        await erased.HoldAsync(person, cancellationToken);
        if (await erased.IsErasedAsync(person, cancellationToken))
        {
            ErasedPersonDropped(logger, message.MembershipId);
            return;
        }

        var membership = MembershipId.From(message.MembershipId);
        var version = MembershipVersion.From(message.Version);
        if (await memberships.HasChangeAsync(membership, version, cancellationToken))
        {
            return;
        }

        memberships.Add(MembershipChange.Of(
            membership,
            person,
            PharmacyId.From(message.PharmacyId),
            version,
            OrganisationRoles.ToRole(message.Role)));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A change of membership {Membership} belongs to an erased person; its event is dropped.")]
    private static partial void ErasedPersonDropped(ILogger logger, Guid membership);
}
