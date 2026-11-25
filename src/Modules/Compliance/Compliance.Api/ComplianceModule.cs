using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api;

public sealed class ComplianceModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<ComplianceModule>(configuration);

        // Removes the dead letters and handled envelopes of an erased person's membership messages that arrive after the
        // erasure's purge (#439, docs/adr/0036), under Compliance:ErasedPersonMessageSweep.
        services.AddErasedPersonMessageSweep(configuration, "Compliance", ComplianceDbContext.Schema, PersonalMessages.Types);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/compliance").MapModuleEndpoints<ComplianceModule>();
}
