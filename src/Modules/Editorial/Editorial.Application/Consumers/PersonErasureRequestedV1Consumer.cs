using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Consumers;

/// <summary>
/// Erases a person in Editorial (#115) and announces it as <see cref="EditorialErasureCompletedV1"/>. It holds the person
/// (<see cref="IErasedPersonStore.HoldAsync"/>, which begins the unit of work's transaction), tombstones them (an unknown
/// person too), forgets their name on every question they approved and every return they made
/// (<see cref="IReviewerNames"/>: two UPDATE statements, run at once inside that transaction), and only after both
/// publishes the completion and commits: the tombstone, the two updates and the completion commit together or not at all.
/// <para>
/// The tombstone is kept although an erased person has no account in the end: their account and tokens outlive the
/// erasure until slice 6, and a next version drafted at the same time copies the names it loaded. An approval, a return
/// and the copy hold the person too, so each either runs before this commit (and its name is forgotten here) or after it
/// (and finds the tombstone and stores no name).
/// </para>
/// <para>
/// It is idempotent by state, not by <c>Metadata.EventIdentifier</c>: a redelivery keeps the first tombstone, finds the
/// names gone, and announces the completion again, which Organisation records once. Two deliveries at once wait for
/// each other under the hold, so the second finds the first's tombstone.
/// </para>
/// </summary>
public sealed class PersonErasureRequestedV1Consumer(IReviewerNames names, IErasedPersonStore erased, IEditorialUnitOfWork unitOfWork, TimeProvider time)
{
    private readonly IReviewerNames names = names;
    private readonly IErasedPersonStore erased = erased;
    private readonly IEditorialUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public async Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        var person = PersonId.From(message.PersonId);

        // Held before anything of the person is read or forgotten, so no approval, return or copy writes the name between
        // the forgetting and the tombstone.
        await erased.HoldAsync(person, cancellationToken);
        if (!await erased.IsErasedAsync(person, cancellationToken))
        {
            erased.Add(ErasedPerson.Of(person, Timestamp.From(time.GetUtcNow())));
        }

        await names.ForgetAsync(person, cancellationToken);

        await unitOfWork.PublishAsync(new EditorialErasureCompletedV1(message.ErasureId));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
