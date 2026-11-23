using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// Sends again the events of every course version Editorial announced, for a subscriber deployed after they went out
/// (ADR 0019), and answers how many of each went. It takes no body and requires editorial.edit, like publishing and
/// retiring, whose announcements it repeats. The literal segment cannot reach <c>/course-versions/{id:guid}</c>.
/// </summary>
public sealed class RepublishEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/course-versions/republish", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
                (await dispatcher.SendAsync(new RepublishCourseVersions(), cancellationToken)).ToHttpResult())
            .RequireAuthorization(EditorialPolicyNames.Edit);
}
