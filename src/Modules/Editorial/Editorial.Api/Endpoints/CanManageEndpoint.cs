using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.CanManage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The permission named editorial.manage when the caller holds it, 403 when not: the module's page asks it before
/// it offers to manage anything, and the integration tests prove the policy on it.
/// </summary>
public sealed class CanManageEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/permissions/manage", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
                (await dispatcher.QueryAsync(new CanManage(), cancellationToken)).ToHttpResult())
            .RequireAuthorization(EditorialPolicyNames.Manage);
}
