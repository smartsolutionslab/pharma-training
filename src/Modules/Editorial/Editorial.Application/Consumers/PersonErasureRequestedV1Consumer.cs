using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Consumers;

/// <summary>
/// Erases a person in Editorial (#115) and announces it as <see cref="EditorialErasureCompletedV1"/>: it forgets their
/// name on every question they approved and every return they made (<see cref="IReviewerNames"/>), then publishes and
/// commits. No course version is loaded or changed, and no tombstone is kept: an erased person has no account to approve
/// or return with again.
/// <para>
/// It is idempotent by state, not by <c>Metadata.EventIdentifier</c>: a redelivery finds the names gone, changes nothing,
/// and announces the completion again, which Organisation records once. An unknown person completes too.
/// </para>
/// </summary>
public sealed class PersonErasureRequestedV1Consumer(IReviewerNames names, IEditorialUnitOfWork unitOfWork)
{
    private readonly IReviewerNames names = names;
    private readonly IEditorialUnitOfWork unitOfWork = unitOfWork;

    public async Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.ErasureId, nameof(message.ErasureId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();

        await names.ForgetAsync(PersonId.From(message.PersonId), cancellationToken);

        await unitOfWork.PublishAsync(new EditorialErasureCompletedV1(message.ErasureId));
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
