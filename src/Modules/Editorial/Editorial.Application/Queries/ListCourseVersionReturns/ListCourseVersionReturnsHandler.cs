using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersionReturns;

public sealed class ListCourseVersionReturnsHandler(ICourseVersionStore versions)
    : IQueryHandler<ListCourseVersionReturns, Result<PagedResult<EditorialCourseVersionReturnDto>, ApiError>>
{
    public async Task<Result<PagedResult<EditorialCourseVersionReturnDto>, ApiError>> HandleAsync(ListCourseVersionReturns query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Version == Guid.Empty)
        {
            return Failure(CourseVersionFailures.NotFound(query.Version));
        }

        var found = await versions.ListReturnsAsync(CourseVersionId.From(query.Version), query.Page, query.PageSize, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(query.Version));
        }

        var page = found.Value;
        return Success(new PagedResult<EditorialCourseVersionReturnDto>([.. page.Items.Select(record => record.ToDto())], page.TotalCount, page.Page, page.PageSize));
    }
}
