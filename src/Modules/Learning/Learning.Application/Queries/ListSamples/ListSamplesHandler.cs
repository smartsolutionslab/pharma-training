using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.ListSamples;

public sealed class ListSamplesHandler(ISampleSummaries samples)
    : IQueryHandler<ListSamples, Result<PagedResult<LearningSampleDto>, ApiError>>
{
    public async Task<Result<PagedResult<LearningSampleDto>, ApiError>> HandleAsync(ListSamples query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var page = await samples.ListAsync(query.Page, query.PageSize, cancellationToken);
        var dtos = new PagedResult<LearningSampleDto>([.. page.Items.Select(sample => sample.ToDto())], page.TotalCount, page.Page, page.PageSize);
        return Success(dtos);
    }
}
