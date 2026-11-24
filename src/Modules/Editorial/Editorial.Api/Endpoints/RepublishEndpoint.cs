using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// Sends again the events of every course version Editorial announced, for a subscriber deployed after they went out
/// (ADR 0019), and answers how many of each went. It takes no body and requires editorial.edit, like publishing and
/// retiring, whose announcements it repeats. One run at a time: a second answers 409 <c>editorial.republish-running</c>.
/// The literal segment cannot reach <c>/course-versions/{id:guid}</c>.
/// </summary>
public sealed class RepublishEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/course-versions/republish", (ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                RepublishAsync(user, dispatcher, cancellationToken))
            .RequireAuthorization(EditorialPolicyNames.Edit);

    /// <summary>The calling editor's run: a caller without a person is refused before anything is dispatched.</summary>
    private static async Task<Results<Ok<EditorialRepublishedEventsDto>, ProblemHttpResult>> RepublishAsync(ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var editor = EditorialCaller.PersonOf(user);
        if (!editor.HasValue)
        {
            return Result<EditorialRepublishedEventsDto, ApiError>.Failure(CourseVersionFailures.IdentityIncomplete()).ToHttpResult();
        }

        return (await dispatcher.SendAsync(new RepublishCourseVersions(editor.Value), cancellationToken)).ToHttpResult();
    }
}
