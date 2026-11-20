using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api;

public sealed class LearningModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleInfrastructure<LearningModule>(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/learning").MapModuleEndpoints<LearningModule>();
}
