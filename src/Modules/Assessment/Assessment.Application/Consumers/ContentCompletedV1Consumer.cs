using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Consumers;

/// <summary>
/// A learner completed a session's content: the first time opens the session's one examination, under the id derived
/// from the session; after three failed attempts, a completion Learning published after the exhaustion unlocks three
/// more. Each event is recorded by its identifier, so a redelivery returns before anything is loaded; and since only a
/// completion that occurred after the last exhaustion unlocks, a replay of an older one grants no attempts even when its
/// marker was never committed. The stream is saved first, then the identifier: a crash in between reruns this, which
/// finds the stream and unlocks nothing (docs/adr/0012). It needs no test definition, so it opens before one arrives.
/// </summary>
public sealed class ContentCompletedV1Consumer(
    IAssessmentAggregateStore examinations,
    IHandledContentCompletions handled,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task HandleAsync(ContentCompletedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.Metadata, nameof(message.Metadata)).IsNotNull();
        Ensure.That(message.Metadata.EventIdentifier, nameof(message.Metadata.EventIdentifier)).IsNotEmpty();
        Ensure.That(message.Metadata.OccurredAt.ToString("O", CultureInfo.InvariantCulture), nameof(message.Metadata.OccurredAt)).Satisfies(
            _ => message.Metadata.OccurredAt != default,
            "A published event carries the instant it occurred.");
        Ensure.That(message.SessionId, nameof(message.SessionId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.CourseId, nameof(message.CourseId)).IsNotEmpty();
        Ensure.That(message.CourseVersionId, nameof(message.CourseVersionId)).IsNotEmpty();

        var eventIdentifier = message.Metadata.EventIdentifier;
        if (await handled.ContainsAsync(eventIdentifier, cancellationToken))
        {
            return;
        }

        var session = LearningSessionId.From(message.SessionId);
        var learner = PersonId.From(message.PersonId);
        var course = CourseId.From(message.CourseId);
        var version = CourseVersionId.From(message.CourseVersionId);
        var id = ExaminationId.For(session);
        var now = Timestamp.From(time.GetUtcNow());

        var loaded = await examinations.LoadAsync<Examination>(id, cancellationToken);
        Examination examination;
        if (loaded.HasValue)
        {
            examination = loaded.Value;
            // A session's examination belongs to one learner, course and version: never unlock someone else's.
            var same = examination.Learner == learner && examination.Course == course && examination.CourseVersion == version;
            Ensure.That(message.SessionId.ToString(), nameof(message.SessionId)).Satisfies(
                _ => same,
                "The session's examination belongs to another learner, course or course version.");

            // Only a completion Learning published after the last exhaustion earns new attempts: a redelivery or a replay of
            // an older one, even one whose handled marker was never committed, finds the examination as it is (docs/adr/0012).
            if (examination.ExhaustedAt is { } exhaustedAt && Timestamp.From(message.Metadata.OccurredAt) > exhaustedAt)
            {
                examination.Unlock(now);
            }
        }
        else
        {
            examination = Examination.Open(id, session, learner, course, version, now);
        }

        await examinations.SaveAsync(examination, cancellationToken);
        handled.Add(new HandledContentCompletion
        {
            EventIdentifier = eventIdentifier,
            SessionId = message.SessionId,
            HandledAt = now.Value,
        });
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
