using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReannounceContentCompletions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api;

public sealed class LearningModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<LearningModule>(configuration);

        // Announces a stored completion whose announcement never left (#277, docs/adr/0016), under Learning:AnnouncementSweep.
        services.AddAnnouncementSweep<ReannounceContentCompletions>(configuration, "Learning");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/learning").MapModuleEndpoints<LearningModule>();
}
