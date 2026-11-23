using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Locks the test of the learner's active assignments of the course when Assessment saw the attempts used up, in every pharmacy.
/// It does not deduplicate on <c>Metadata.EventIdentifier</c>: the assignment applies the event by when it happened
/// (<c>Metadata.OccurredAt</c>, docs/adr/0019), so a second delivery, a replay and a reordering change nothing by
/// themselves, which is stronger, since it also absorbs an old event under a new identifier. A save that lost a race
/// throws, and the retry reloads and applies it again.
/// </summary>
public sealed class AttemptsExhaustedV1Consumer(IAssignmentStore assignments, IComplianceUnitOfWork unitOfWork)
{
    public async Task HandleAsync(AttemptsExhaustedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.SessionId, nameof(message.SessionId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();

        var active = await assignments.ActiveOfLearnerAsync(PersonId.From(message.PersonId), CourseId.From(message.CourseId), cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var at = Timestamp.From(message.Metadata.OccurredAt);
        foreach (var assignment in active)
        {
            assignment.MarkLocked(at);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
