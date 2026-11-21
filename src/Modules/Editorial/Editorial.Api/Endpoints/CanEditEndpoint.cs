using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.CanEdit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The permission named editorial.edit when the caller holds it, 403 when not: it requires the realm role editor,
/// which platform editors hold, and the integration tests prove the policy on it.
/// </summary>
public sealed class CanEditEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/permissions/edit", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
                (await dispatcher.QueryAsync(new CanEdit(), cancellationToken)).ToHttpResult())
            .RequireAuthorization(EditorialPolicyNames.Edit);
}
