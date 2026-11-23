using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetCurrentAttempt;

/// <summary>Stub for the red run of #46: not implemented yet.</summary>
public sealed class GetCurrentAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies)
    : IQueryHandler<GetCurrentAttempt, Result<AssessmentAttemptDto, ApiError>>
{
    public Task<Result<AssessmentAttemptDto, ApiError>> HandleAsync(GetCurrentAttempt query, CancellationToken cancellationToken)
    {
        _ = (store, copies);
        throw new NotImplementedException();
    }
}
