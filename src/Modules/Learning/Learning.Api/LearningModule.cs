using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReannounceContentCompletions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Infrastructure.Persistence;
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

        // Removes the dead letters and handled envelopes of an erased person's membership messages that arrive after the
        // erasure's purge (#439, docs/adr/0036), under Learning:ErasedPersonMessageSweep.
        services.AddErasedPersonMessageSweep(configuration, "Learning", LearningDbContext.Schema, PersonalMessages.Types);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/learning").MapModuleEndpoints<LearningModule>();
}
