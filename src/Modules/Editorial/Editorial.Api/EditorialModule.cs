using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

public sealed class EditorialModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<EditorialModule>(configuration);

        // Read once, here: a value the host cannot read stops it at startup, never on a publish (ADR 0005).
        services.AddSingleton(EditorialSettings.ReviewFrom(configuration));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/editorial").MapModuleEndpoints<EditorialModule>();
}
