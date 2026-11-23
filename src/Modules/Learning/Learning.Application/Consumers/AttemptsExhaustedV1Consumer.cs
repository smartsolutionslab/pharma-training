using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Reopens the chapters of the wrong answers when Assessment locks a session's test (ADR 0025). The session notes the
/// announcement's <c>Metadata.EventIdentifier</c> in the same append as the reopen, so the same lock delivered again acts
/// once, even after the learner worked the chapters through anew; a finished session ignores every lock, since a passed
/// test outranks it. Memberships and the version's retirement are not consulted: the outcome concerns an existing
/// session, not a learner's request. A fault throws, so it is retried and then dead-lettered, never dropped: a malformed
/// message, an unknown session, one whose learner, course or version differs, a chapter outside the session, or an
/// append that lost its race, whose retry reloads the stream. Nothing is published: Assessment unlocks on the next
/// <c>ContentCompletedV1</c>.
/// </summary>
public sealed class AttemptsExhaustedV1Consumer(ILearningAggregateStore sessions, TimeProvider time)
{
    public async Task HandleAsync(AttemptsExhaustedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.Metadata, nameof(message.Metadata)).IsNotNull();
        Ensure.That(message.Metadata.EventIdentifier, nameof(EventMetadata.EventIdentifier)).IsNotEmpty();
        Ensure.That(message.SessionId, nameof(message.SessionId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();
        Ensure.That(message.FailedChapterIds, nameof(message.FailedChapterIds)).IsNotNull();
        Ensure.That(message.FailedChapterIds.Count, nameof(message.FailedChapterIds)).AtLeast(1);
        foreach (var chapter in message.FailedChapterIds)
        {
            Ensure.That(chapter, nameof(message.FailedChapterIds)).IsNotEmpty();
        }

        var session = await TestOutcomeSessions.LoadAsync(
            sessions, "AttemptsExhausted", message.SessionId, message.PersonId, message.CourseId, message.CourseVersionId, cancellationToken);

        var notice = ExhaustionNotice.From(message.Metadata.EventIdentifier);
        var reopened = session.ReopenChapters(notice, [.. message.FailedChapterIds.Select(ChapterId.From)], Timestamp.From(time.GetUtcNow()));
        if (reopened.IsFailure)
        {
            var detail = reopened.Error is ChapterNotInCourse notInCourse ? $" Chapter {notInCourse.Chapter} is not in it." : string.Empty;
            throw new InvalidOperationException(
                $"AttemptsExhausted {notice} cannot reopen the chapters of session {message.SessionId}: {reopened.Error.Code}.{detail}");
        }

        if (((IEventSourced)session).PendingEvents.Count > 0)
        {
            await sessions.SaveAsync(session, cancellationToken);
        }
    }
}
