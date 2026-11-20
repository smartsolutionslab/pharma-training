using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListSamples;

public sealed class ListSamplesHandler(ISampleStore samples)
    : IQueryHandler<ListSamples, Result<PagedResult<ComplianceSampleDto>, ApiError>>
{
    public async Task<Result<PagedResult<ComplianceSampleDto>, ApiError>> HandleAsync(ListSamples query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var page = await samples.ListAsync(query.Page, query.PageSize, cancellationToken);
        var dtos = new PagedResult<ComplianceSampleDto>([.. page.Items.Select(sample => sample.ToDto())], page.TotalCount, page.Page, page.PageSize);
        return Success(dtos);
    }
}
