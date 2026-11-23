using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationBySession;

/// <summary>Stub for the red run of #46: not implemented yet.</summary>
public sealed class GetExaminationBySessionHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies)
    : IQueryHandler<GetExaminationBySession, Result<AssessmentExaminationDto, ApiError>>
{
    public Task<Result<AssessmentExaminationDto, ApiError>> HandleAsync(GetExaminationBySession query, CancellationToken cancellationToken)
    {
        _ = (store, copies);
        throw new NotImplementedException();
    }
}
