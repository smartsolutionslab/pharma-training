using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetLearningSession;

/// <summary>
/// Reads the session's view and the reading order of its version's copy. A session that is unknown, the empty id, or
/// another person's is not found alike, so the answer reveals nothing of other people's sessions.
/// </summary>
public sealed class GetLearningSessionHandler(ILearningSessions sessions, ICourseVersionCopies versions)
    : IQueryHandler<GetLearningSession, Result<LearningSessionDto, ApiError>>
{
    public async Task<Result<LearningSessionDto, ApiError>> HandleAsync(GetLearningSession query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(LearningSessionFailures.SessionNotFound(query.Id));
        }

        var found = await sessions.FindAsync(LearningSessionId.From(query.Id), cancellationToken);
        if (!found.HasValue || found.Value.LearnerId != query.Learner)
        {
            return Failure(LearningSessionFailures.SessionNotFound(query.Id));
        }

        var session = found.Value;
        var version = await versions.FindAsync(CourseVersionId.From(session.CourseVersionId), cancellationToken);
        if (!version.HasValue)
        {
            // A session starts only on a version Learning holds, and a copy is never removed: this is a broken store.
            throw new InvalidOperationException($"Session {session.Id} is on course version {session.CourseVersionId}, which Learning does not hold.");
        }

        return Success(session.ToDto(version.Value));
    }
}
