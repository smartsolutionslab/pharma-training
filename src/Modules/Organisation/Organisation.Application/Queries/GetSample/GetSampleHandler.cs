using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Samples;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetSample;

public sealed class GetSampleHandler(ISampleStore samples)
    : IQueryHandler<GetSample, Result<OrganisationSampleDto, ApiError>>
{
    public async Task<Result<OrganisationSampleDto, ApiError>> HandleAsync(GetSample query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(SampleFailures.NotFound(query.Id));
        }

        var sample = await samples.FindAsync(SampleId.From(query.Id), cancellationToken);
        if (!sample.HasValue)
        {
            return Failure(SampleFailures.NotFound(query.Id));
        }

        return Success(sample.Value.ToDto());
    }
}
