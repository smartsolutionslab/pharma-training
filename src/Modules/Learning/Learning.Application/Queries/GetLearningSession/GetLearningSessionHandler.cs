using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetLearningSession;

/// <summary>
/// Reads the session's view and the reading order of its version's copy. A learner reads their own session whatever
/// their membership, so a former member still reads it (docs/adr/0017). Any other id, unknown, empty or another
/// person's, is not found alike for an active member, and refused as no active member for anyone else, so the answer
/// reveals nothing of other people's sessions.
/// </summary>
public sealed class GetLearningSessionHandler(ILearningSessions sessions, ICourseVersionCopies versions, IMembershipCopies memberships)
    : IQueryHandler<GetLearningSession, Result<LearningSessionDto, ApiError>>
{
    public async Task<Result<LearningSessionDto, ApiError>> HandleAsync(GetLearningSession query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var found = query.Id == Guid.Empty
            ? Option<LearningSessionView>.None
            : await sessions.FindAsync(LearningSessionId.From(query.Id), cancellationToken);
        if (!found.HasValue || found.Value.LearnerId != query.Learner)
        {
            return await NotTheCallersAsync(query, cancellationToken);
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

    /// <summary>Not found for an active member, as before; anyone else is told that they are no active member.</summary>
    private async Task<Result<LearningSessionDto, ApiError>> NotTheCallersAsync(GetLearningSession query, CancellationToken cancellationToken)
    {
        var access = await LearnerAccess.RequireActiveMemberAsync(memberships, PersonId.From(query.Learner), cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        return Failure(LearningSessionFailures.SessionNotFound(query.Id));
    }
}
