using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

public static class ModuleEndpoints
{
    /// <summary>Maps one module's endpoints into the route group the module was given; a refusal to retry later answers <c>Retry-After</c> (<see cref="RetryAfterHeaderFilter"/>).</summary>
    public static RouteGroupBuilder MapModuleEndpoints<TModule>(this RouteGroupBuilder group)
        where TModule : IModule
    {
        Ensure.That(group).IsNotNull();
        group.RequireAuthorization();
        group.AddEndpointFilter<RetryAfterHeaderFilter>();
        group.MapEndpointsIn(typeof(TModule).Assembly);
        return group;
    }
}
