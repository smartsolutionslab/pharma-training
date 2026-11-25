using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Erases a person in Learning (#115) and announces it as <see cref="LearningErasureCompletedV1"/>. Learning's tables
/// hold the person's id only, the pseudonym that stays, so it purges the stored membership messages that carry their name
/// (<see cref="IPersonalMessagePurge"/>) before it publishes and commits; the purge runs on a connection of its own and
/// commits by itself, so a failure leaves the completion unannounced and the retry purges again.
/// <para>
/// It is idempotent by state, not by <c>Metadata.EventIdentifier</c>: a redelivery purges again, which finds nothing, and
/// announces the completion again, which Organisation records once. An unknown person completes too.
/// </para>
/// </summary>
public sealed class PersonErasureRequestedV1Consumer(IErasedPersonStore erased, IPersonalMessagePurge purge, ILearningUnitOfWork unitOfWork, TimeProvider time)
{
    // Not used yet: the #421 review's red tests (#115) ask for the person to be held and tombstoned at Learning's clock.
    private readonly IErasedPersonStore erased = erased;
    private readonly TimeProvider time = time;

    /// <summary>The messages Learning stores that carry a person's name.</summary>
    private static readonly IReadOnlyCollection<Type> PersonalMessages = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];

    private readonly IPersonalMessagePurge purge = purge;
    private readonly ILearningUnitOfWork unitOfWork = unitOfWork;

    public async Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        await purge.PurgeAsync(message.PersonId, PersonalMessages, cancellationToken);

        await unitOfWork.PublishAsync(new LearningErasureCompletedV1(message.ErasureId));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
