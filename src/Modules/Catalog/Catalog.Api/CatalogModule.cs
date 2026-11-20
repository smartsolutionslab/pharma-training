using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Api;

public sealed class CatalogModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleInfrastructure<CatalogModule>(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/catalog").MapModuleEndpoints<CatalogModule>();
}
