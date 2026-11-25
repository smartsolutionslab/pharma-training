using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Erases a person in Learning (#115) and announces it as <see cref="LearningErasureCompletedV1"/>. Learning's tables
/// hold the person's id only, the pseudonym that stays, so it tombstones the person (an unknown one too, at Learning's
/// clock, so a membership message arriving later is dropped; the person is held first, as the membership consumers hold
/// them) and purges the stored membership messages that carry their name
/// (<see cref="IPersonalMessagePurge"/>) before it publishes and commits; the purge runs on a connection of its own and
/// commits by itself, so a failure leaves the completion unannounced and the retry purges again.
/// <para>
/// It is idempotent by state, not by <c>Metadata.EventIdentifier</c>: a redelivery keeps the first tombstone, purges
/// again, which finds nothing, and announces the completion again, which Organisation records once. An unknown person
/// completes too.
/// </para>
/// </summary>
public sealed class PersonErasureRequestedV1Consumer(IErasedPersonStore erased, IPersonalMessagePurge purge, ILearningUnitOfWork unitOfWork, TimeProvider time)
{
    private readonly IErasedPersonStore erased = erased;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly ILearningUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public async Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        // Held before the tombstone is read, so a membership event running at the same time either commits before this
        // (and its stored copy is purged here) or waits and then finds the tombstone and is dropped.
        var person = PersonId.From(message.PersonId);
        await erased.HoldAsync(person, cancellationToken);
        if (!await erased.IsErasedAsync(person, cancellationToken))
        {
            erased.Add(new ErasedPersonCopy { PersonId = message.PersonId, ErasedAt = time.GetUtcNow() });
        }

        await purge.PurgeAsync(message.PersonId, PersonalMessages.Types, cancellationToken);

        await unitOfWork.PublishAsync(new LearningErasureCompletedV1(message.ErasureId));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
