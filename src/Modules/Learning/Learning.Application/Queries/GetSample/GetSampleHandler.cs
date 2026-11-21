using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetSample;

public sealed class GetSampleHandler(ISampleSummaries samples)
    : IQueryHandler<GetSample, Result<LearningSampleDto, ApiError>>
{
    public async Task<Result<LearningSampleDto, ApiError>> HandleAsync(GetSample query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var sample = await samples.FindAsync(query.Id, cancellationToken);
        if (!sample.HasValue)
        {
            return Failure(SampleFailures.NotFound(query.Id));
        }

        return Success(sample.Value.ToDto());
    }
}
