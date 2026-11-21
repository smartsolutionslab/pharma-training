using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.ListCourses;

public sealed class ListCoursesHandler(ICourseVersionStore versions)
    : IQueryHandler<ListCourses, Result<PagedResult<CatalogCourseDto>, ApiError>>
{
    public async Task<Result<PagedResult<CatalogCourseDto>, ApiError>> HandleAsync(ListCourses query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var page = await versions.ListCurrentAsync(query.Page, query.PageSize, cancellationToken);
        var dtos = new PagedResult<CatalogCourseDto>([.. page.Items.Select(version => version.ToCatalogCourseDto())], page.TotalCount, page.Page, page.PageSize);
        return Success(dtos);
    }
}
