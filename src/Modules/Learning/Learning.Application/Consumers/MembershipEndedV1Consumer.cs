using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores that Organisation ended a membership, once, whether the membership's start has arrived yet or not: the member
/// is kept, and the membership reads as ended whenever it is there. It deduplicates on the membership id, not on
/// <c>Metadata.EventIdentifier</c>, for the reason <see cref="MembershipStartedV1Consumer"/> gives. Copied from
/// Compliance's <c>Compliance.Application/Consumers/MembershipEndedV1Consumer.cs</c>.
/// </summary>
/// <remarks>
/// The handler takes the message and a cancellation token only; everything else comes by constructor, since the
/// AppHost's messaging topology reads every parameter type of a consumer's handler (docs/adr/0011).
/// </remarks>
public sealed class MembershipEndedV1Consumer(IMembershipCopies memberships, ILearningUnitOfWork unitOfWork)
{
    public async Task HandleAsync(MembershipEndedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();

        if (await memberships.IsEndedAsync(MembershipId.From(message.MembershipId), cancellationToken))
        {
            return;
        }

        memberships.Add(new MembershipEndingCopy { Id = message.MembershipId, PersonId = message.PersonId, PharmacyId = message.PharmacyId });
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
