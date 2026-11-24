using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;

/// <summary>
/// Puts the learner's open assignments of the course in progress when Learning started a session, in every pharmacy.
/// It does not deduplicate on <c>Metadata.EventIdentifier</c>: the assignment applies the event by when it happened
/// (<c>Metadata.OccurredAt</c>, docs/adr/0023), so a second delivery, a replay and a reordering change nothing by
/// themselves, which is stronger, since it also absorbs an old event under a new identifier. A save that lost a race
/// throws, and the retry reloads and applies it again.
/// It also keeps a copy of the start, whether or not the person has an active assignment of the course, from which the
/// dashboard tells the running session's version (docs/adr/0028); that copy is deduplicated on the session id, and two
/// deliveries at once race only on its primary key: the loser throws and its retry finds the copy.
/// </summary>
public sealed class LearningSessionStartedV1Consumer(IAssignmentStore assignments, ILearnerProgressStore progress, IComplianceUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task HandleAsync(LearningSessionStartedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();
        Ensure.That(message.SessionId, nameof(message.SessionId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();

        var session = LearningSessionId.From(message.SessionId);
        var person = PersonId.From(message.PersonId);
        var course = CourseId.From(message.CourseId);
        // A publisher's clock ahead of Compliance's would otherwise hold off every later lock or unlock: an event
        // stamped in the future counts as having happened when it was received (docs/adr/0023); the copy keeps that too.
        var received = time.GetUtcNow();
        var at = Timestamp.From(message.Metadata.OccurredAt < received ? message.Metadata.OccurredAt : received);
        var kept = false;
        if (!await progress.HasSessionStartAsync(session, cancellationToken))
        {
            progress.Add(LearningSessionStart.Of(session, person, course, CourseVersionId.From(message.CourseVersionId), at));
            kept = true;
        }

        var active = await assignments.ActiveOfLearnerAsync(person, course, cancellationToken);
        if (!kept && active.Count == 0)
        {
            return;
        }

        foreach (var assignment in active)
        {
            assignment.MarkStarted(at);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
