using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Stores a membership Organisation started, once. A membership starts once, so a member already stored means its event
/// was handled, whatever the event's identifier or payload: a second delivery changes nothing, and the first one wins.
/// It deduplicates on the membership id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// </summary>
public sealed partial class MembershipStartedV1Consumer(IMemberStore members, IErasedPersonStore erased, IComplianceUnitOfWork unitOfWork, ILogger<MembershipStartedV1Consumer> logger)
{
    public async Task HandleAsync(MembershipStartedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        ConsumerGuards.IsText(message.DisplayName, nameof(message.DisplayName), MemberName.MaxLength);
        Ensure.That((int)message.JobFunction, nameof(message.JobFunction)).Satisfies(value => Enum.IsDefined((JobFunctionV1)value), "is no job function Compliance knows.");
        Ensure.That((int)message.Role, nameof(message.Role)).Satisfies(value => Enum.IsDefined((MembershipRoleV1)value), "is no role Compliance knows.");

        // A person erased (#115) stays erased: a redelivery or a replayed dead letter must not write the name back.
        if (await erased.IsErasedAsync(PersonId.From(message.PersonId), cancellationToken))
        {
            ErasedPersonDropped(logger, message.MembershipId);
            return;
        }

        var membership = MembershipId.From(message.MembershipId);
        if (await members.ExistsAsync(membership, cancellationToken))
        {
            return;
        }

        members.Add(Member.Started(
            membership,
            PersonId.From(message.PersonId),
            PharmacyId.From(message.PharmacyId),
            MemberName.From(message.DisplayName),
            OrganisationValues.ToJobFunction(message.JobFunction),
            OrganisationValues.ToRole(message.Role)));
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Membership {Membership} belongs to an erased person; its event is dropped.")]
    private static partial void ErasedPersonDropped(ILogger logger, Guid membership);
}
