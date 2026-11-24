using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationBySession;

/// <summary>
/// Loads the examination of the learning session by the id derived from it (ADR 0012), read only, with the version's
/// copy if it has arrived. Another person's examination, an unknown session and the empty Guid are not found.
/// </summary>
public sealed class GetExaminationBySessionHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies)
    : IQueryHandler<GetExaminationBySession, Result<AssessmentExaminationDto, ApiError>>
{
    public async Task<Result<AssessmentExaminationDto, ApiError>> HandleAsync(GetExaminationBySession query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.SessionId == Guid.Empty)
        {
            return Failure(ExaminationFailures.NotFound(query.SessionId));
        }

        var loaded = await store.LoadAsync<Examination>(ExaminationId.For(LearningSessionId.From(query.SessionId)), cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner.Value != query.Learner)
        {
            return Failure(ExaminationFailures.NotFound(query.SessionId));
        }

        var copy = await copies.FindAsync(loaded.Value.CourseVersion, cancellationToken);
        return Success(loaded.Value.ToAssessmentExaminationDto(copy));
    }
}
