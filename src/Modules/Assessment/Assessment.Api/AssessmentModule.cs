using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api;

public sealed class AssessmentModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleInfrastructure<AssessmentModule>(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/assessment").MapModuleEndpoints<AssessmentModule>();
}
