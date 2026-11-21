using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersions;

public sealed class ListCourseVersionsHandler(ICourseVersionStore versions)
    : IQueryHandler<ListCourseVersions, Result<PagedResult<EditorialCourseVersionSummaryDto>, ApiError>>
{
    public async Task<Result<PagedResult<EditorialCourseVersionSummaryDto>, ApiError>> HandleAsync(ListCourseVersions query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var page = await versions.ListAsync(query.Status, query.Page, query.PageSize, cancellationToken);
        var dtos = new PagedResult<EditorialCourseVersionSummaryDto>([.. page.Items.Select(version => version.ToSummaryDto())], page.TotalCount, page.Page, page.PageSize);
        return Success(dtos);
    }
}
