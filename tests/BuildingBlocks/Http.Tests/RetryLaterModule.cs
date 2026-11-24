using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http.Tests;

/// <summary>A module whose endpoints (<see cref="RetryLaterEndpoints"/>) are mapped the way every module's are, through <see cref="ModuleEndpoints.MapModuleEndpoints{TModule}"/>.</summary>
public sealed class RetryLaterModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/refusals").MapModuleEndpoints<RetryLaterModule>();
}
