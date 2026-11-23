using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetAttemptResult;

/// <summary>Stub for the red run of #46: not implemented yet.</summary>
public sealed class GetAttemptResultHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies)
    : IQueryHandler<GetAttemptResult, Result<AssessmentAttemptResultDto, ApiError>>
{
    public Task<Result<AssessmentAttemptResultDto, ApiError>> HandleAsync(GetAttemptResult query, CancellationToken cancellationToken)
    {
        _ = (store, copies);
        throw new NotImplementedException();
    }
}
