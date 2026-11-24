using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Finds the session one of Assessment's test outcomes names. A session Learning does not hold, or one whose learner,
/// course or version differs from the outcome, is a fault that throws: it cannot become valid later.
/// </summary>
internal static class TestOutcomeSessions
{
    public static async Task<LearningSession> LoadAsync(
        ILearningAggregateStore sessions,
        string outcome,
        Guid sessionId,
        Guid personId,
        Guid courseId,
        Guid courseVersionId,
        CancellationToken cancellationToken)
    {
        var loaded = await sessions.LoadAsync<LearningSession>(LearningSessionId.From(sessionId), cancellationToken);
        if (!loaded.HasValue)
        {
            throw new InvalidOperationException($"{outcome} names session {sessionId}, but Learning holds no session {sessionId}.");
        }

        var session = loaded.Value;
        Compare(outcome, sessionId, "PersonId", personId, session.Learner.Value);
        Compare(outcome, sessionId, "CourseId", courseId, session.Course.Value);
        Compare(outcome, sessionId, "CourseVersionId", courseVersionId, session.CourseVersion.Value);
        return session;
    }

    private static void Compare(string outcome, Guid sessionId, string field, Guid named, Guid held)
    {
        if (named != held)
        {
            throw new InvalidOperationException($"{outcome} for session {sessionId} names {field} {named}, but the session holds {held}.");
        }
    }
}
