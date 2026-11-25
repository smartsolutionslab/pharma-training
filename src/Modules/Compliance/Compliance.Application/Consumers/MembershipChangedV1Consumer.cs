using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Stores a change Organisation made to a membership, once per version, beside the member and whether or not the member
/// has arrived yet; which state is current is decided when it is read (docs/adr/0015). A change already stored at that
/// version means its event was handled, so a second delivery changes nothing and the first one wins. It deduplicates on
/// the membership and the version, not on <c>Metadata.EventIdentifier</c>, for the reason <see cref="MembershipStartedV1Consumer"/> gives.
/// </summary>
/// <remarks>
/// The handler takes the message and a cancellation token only; everything else comes by constructor, since the
/// AppHost's messaging topology reads every parameter type of a consumer's handler (docs/adr/0011).
/// </remarks>
public sealed partial class MembershipChangedV1Consumer(IMemberStore members, IErasedPersonStore erased, IComplianceUnitOfWork unitOfWork, ILogger<MembershipChangedV1Consumer> logger)
{
    public async Task HandleAsync(MembershipChangedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        ConsumerGuards.IsText(message.DisplayName, nameof(message.DisplayName), MemberName.MaxLength);
        Ensure.That((int)message.JobFunction, nameof(message.JobFunction)).Satisfies(value => Enum.IsDefined((JobFunctionV1)value), "is no job function Compliance knows.");
        Ensure.That((int)message.Role, nameof(message.Role)).Satisfies(value => Enum.IsDefined((MembershipRoleV1)value), "is no role Compliance knows.");
        Ensure.That(message.Version, nameof(message.Version)).AtLeast(2);

        // A person erased (#115) stays erased: a redelivery or a replayed dead letter must not write the name back. The
        // person is held first, so an erasure running at the same time cannot commit its tombstone between this check
        // and the write below.
        await erased.HoldAsync(PersonId.From(message.PersonId), cancellationToken);
        if (await erased.IsErasedAsync(PersonId.From(message.PersonId), cancellationToken))
        {
            ErasedPersonDropped(logger, message.MembershipId);
            return;
        }

        var membership = MembershipId.From(message.MembershipId);
        var version = MembershipVersion.From(message.Version);
        if (await members.HasChangeAsync(membership, version, cancellationToken))
        {
            return;
        }

        members.Add(MembershipChange.Of(
            membership,
            PersonId.From(message.PersonId),
            PharmacyId.From(message.PharmacyId),
            version,
            MemberName.From(message.DisplayName),
            OrganisationValues.ToJobFunction(message.JobFunction),
            OrganisationValues.ToRole(message.Role)));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Membership {Membership} belongs to an erased person; its event is dropped.")]
    private static partial void ErasedPersonDropped(ILogger logger, Guid membership);
}
