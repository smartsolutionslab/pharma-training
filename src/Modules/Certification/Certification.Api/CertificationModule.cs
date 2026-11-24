using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

public sealed class CertificationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<CertificationModule>(configuration);

        // The per-address limit on public verification, read once here: a value the host cannot read stops it at
        // startup (docs/adr/0029).
        services.AddUserRateLimit(CertificationVerificationLimits.From(configuration));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/certification").MapModuleEndpoints<CertificationModule>();
}
