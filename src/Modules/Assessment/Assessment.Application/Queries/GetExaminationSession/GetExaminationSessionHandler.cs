using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationSession;

/// <summary>Loads the examination read only and answers its learning session; an unknown one and another person's are not found.</summary>
public sealed class GetExaminationSessionHandler(IAssessmentAggregateStore store)
    : IQueryHandler<GetExaminationSession, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(GetExaminationSession query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.ExaminationId == Guid.Empty)
        {
            return Failure(ExaminationFailures.NotFound(query.ExaminationId));
        }

        var loaded = await store.LoadAsync<Examination>(ExaminationId.From(query.ExaminationId), cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner.Value != query.Learner)
        {
            return Failure(ExaminationFailures.NotFound(query.ExaminationId));
        }

        return Success(loaded.Value.Session.Value);
    }
}
