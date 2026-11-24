using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;

/// <summary>
/// What Assessment announces of its examinations. Publishing fills each event's metadata. <c>SessionId</c> is always the
/// learning session that opened the examination, never the examination's own id.
/// </summary>
public static class AssessmentIntegrationEventMappings
{
    /// <summary>A pass, for Learning's session.</summary>
    public static TestPassedV1 ToIntegrationEvent(this ExaminationPassed passed)
    {
        Ensure.That(passed).IsNotNull();

        return new TestPassedV1(
            passed.Attempt.Value,
            passed.Session.Value,
            passed.Learner.Value,
            passed.Course.Value,
            passed.CourseVersion.Value,
            passed.Score.Value,
            passed.PassedAt.Value);
    }

    /// <summary>A failed attempt of <paramref name="examination"/>, with its number since the last unlock; a passed one is no failure.</summary>
    public static TestFailedV1 ToIntegrationEvent(this AttemptSubmitted submitted, Examination examination)
    {
        Ensure.That(submitted).IsNotNull();
        Ensure.That(examination).IsNotNull();
        Ensure.That(submitted.Outcome.ToString(), nameof(submitted.Outcome)).Satisfies(
            _ => submitted.Outcome == AttemptOutcome.Failed,
            "Only a failed attempt is announced as TestFailedV1.");

        var attempt = examination.Attempts.Single(candidate => candidate.Id == submitted.Attempt);
        return new TestFailedV1(
            submitted.Attempt.Value,
            examination.Session.Value,
            examination.Learner.Value,
            examination.Course.Value,
            examination.CourseVersion.Value,
            attempt.Number.Value);
    }

    /// <summary>The lock of <paramref name="examination"/>, for Learning to reopen the failed chapters and Compliance to show it; the course is the examination's, which the stored event does not carry.</summary>
    public static AttemptsExhaustedV1 ToIntegrationEvent(this ExaminationExhausted exhausted, Examination examination)
    {
        Ensure.That(exhausted).IsNotNull();
        Ensure.That(examination).IsNotNull();

        return new AttemptsExhaustedV1(
            exhausted.Session.Value,
            exhausted.Learner.Value,
            examination.Course.Value,
            exhausted.CourseVersion.Value,
            [.. exhausted.FailedChapters.Select(chapter => chapter.Value)]);
    }
}
