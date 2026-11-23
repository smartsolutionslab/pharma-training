using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>
/// Stores that Organisation ended a membership, once, whether the membership's start has arrived yet or not: the
/// membership is kept, and still names the holder of a certificate.
/// It deduplicates on the membership id, not on <c>Metadata.EventIdentifier</c>: for a fact only ever inserted that is stronger, since it also absorbs a re-publish under a new identifier.
/// </summary>
public sealed class MembershipEndedV1Consumer(IMembershipStore memberships, ICertificationUnitOfWork unitOfWork)
{
    public async Task HandleAsync(MembershipEndedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();

        var membership = MembershipId.From(message.MembershipId);
        if (await memberships.IsEndedAsync(membership, cancellationToken))
        {
            return;
        }

        memberships.Add(MembershipEnding.Of(membership, PersonId.From(message.PersonId), PharmacyId.From(message.PharmacyId), Timestamp.From(message.EndedAt)));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
