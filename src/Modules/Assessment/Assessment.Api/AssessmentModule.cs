using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestPasses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api;

public sealed class AssessmentModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<AssessmentModule>(configuration);

        // Announces a stored test pass whose announcement never left (#277, docs/adr/0016), under Assessment:AnnouncementSweep.
        services.AddAnnouncementSweep<ReannounceTestPasses>(configuration, "Assessment");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/assessment").MapModuleEndpoints<AssessmentModule>();
}
