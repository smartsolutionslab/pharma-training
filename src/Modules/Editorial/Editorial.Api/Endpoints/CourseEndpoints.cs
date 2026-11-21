using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.CreateCourse;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>An editor starts a course: its first version, a draft, whose id the answer carries.</summary>
public sealed class CourseEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/courses", async (CreateEditorialCourseRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var editor = EditorialCaller.PersonOf(user);
            if (!editor.HasValue)
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.IdentityIncomplete()).ToCreatedHttpResult(CourseVersionEndpoints.Location);
            }

            // A title the caller got wrong is refused before it becomes a value object, whose factory would throw on it.
            if (!CourseTitle.TryFrom(request.Title, out var title))
            {
                return Result<Guid, ApiError>.Failure(CourseVersionFailures.InvalidCourseTitle()).ToCreatedHttpResult(CourseVersionEndpoints.Location);
            }

            return (await dispatcher.SendAsync(new CreateCourse(title, editor.Value), cancellationToken)).ToCreatedHttpResult(CourseVersionEndpoints.Location);
        }).RequireAuthorization(EditorialPolicyNames.Edit);
    }
}
