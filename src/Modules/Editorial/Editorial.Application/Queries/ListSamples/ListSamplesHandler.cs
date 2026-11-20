using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListSamples;

public sealed class ListSamplesHandler(ISampleStore samples)
    : IQueryHandler<ListSamples, Result<PagedResult<EditorialSampleDto>, ApiError>>
{
    public async Task<Result<PagedResult<EditorialSampleDto>, ApiError>> HandleAsync(ListSamples query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var page = await samples.ListAsync(query.Page, query.PageSize, cancellationToken);
        var dtos = new PagedResult<EditorialSampleDto>([.. page.Items.Select(sample => sample.ToDto())], page.TotalCount, page.Page, page.PageSize);
        return Success(dtos);
    }
}
