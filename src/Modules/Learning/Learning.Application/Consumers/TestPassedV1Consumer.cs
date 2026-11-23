using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Finishes the session when Assessment reports its test passed, at the time it was passed, from any state: the graded
/// examination is what counts (ADR 0018), so a passed test outranks a lock that arrived before or after it (ADR 0020).
/// A finished session never changes again, so a second delivery or a republish under a new identifier stores nothing.
/// Memberships and the version's retirement are not consulted. A fault throws, so it is retried and then dead-lettered,
/// never dropped: a malformed message, an unknown session, one whose learner, course or version differs, or an append
/// that lost its race, whose retry reloads the stream. Nothing is published.
/// </summary>
public sealed class TestPassedV1Consumer(ILearningAggregateStore sessions)
{
    public async Task HandleAsync(TestPassedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.SessionId, nameof(message.SessionId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();

        var session = await TestOutcomeSessions.LoadAsync(
            sessions, "TestPassed", message.SessionId, message.PersonId, message.CourseId, message.CourseVersionId, cancellationToken);

        session.Finish(Timestamp.From(message.PassedAt));
        if (((IEventSourced)session).PendingEvents.Count > 0)
        {
            await sessions.SaveAsync(session, cancellationToken);
        }
    }
}
