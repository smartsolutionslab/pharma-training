using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.GetCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>The editors' inbox: the course versions a page at a time, of one status or of all, and one version with its chapters.</summary>
public sealed class CourseVersionEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/course-versions", async (EditorialCourseVersionStatus? status, Page? page, PageSize? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // The enum's converter also reads numbers, so an undefined one is the caller's mistake too, not a bug.
            if (status is { } given && !Enum.IsDefined(given))
            {
                return Result<PagedResult<EditorialCourseVersionSummaryDto>, ApiError>.Failure(CourseVersionFailures.InvalidCourseVersionStatus()).ToHttpResult();
            }

            var query = new ListCourseVersions(status?.ToDomain(), page ?? Page.First, pageSize ?? PageSize.Default);
            return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapGet("/course-versions/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new GetCourseVersion(id), cancellationToken)).ToHttpResult())
            .RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>Where a version is read back; a chapter has no resource of its own, so it is the version that holds it.</summary>
    internal static string Location(Guid id) => $"/editorial/course-versions/{id}";
}
