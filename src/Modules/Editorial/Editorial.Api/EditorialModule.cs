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

        // Read once, here: a value the host cannot read stops it at startup, never on a publish (ADR 0007).
        services.AddSingleton(EditorialSettings.ReviewFrom(configuration));

        // The per-editor limits on the endpoints that call the model, read once here like the line above (docs/adr/0020).
        foreach (var limit in EditorialModelLimits.From(configuration))
        {
            services.AddUserRateLimit(limit);
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/editorial").MapModuleEndpoints<EditorialModule>();
}
