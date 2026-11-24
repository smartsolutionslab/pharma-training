using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

public sealed class CertificationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<CertificationModule>(configuration);

        // The certificate's PDF (#50, docs/adr/0030): the renderer, which has no DbContext and so is no store found by
        // convention, and the links into the web app for the verification link it prints (Frontend:BaseUrl, which the
        // AppHost gives a host whose project says <LinksToFrontend>). Certification sends no mail, so it takes the links
        // alone, without AddModuleMail's SMTP settings. The create-only blob writes come with AddModuleInfrastructure.
        services.TryAddSingleton<ICertificateRenderer, CertificatePdfRenderer>();
        services.TryAddSingleton<IFrontendLinks>(_ => new FrontendLinks(configuration));

        // The per-address limit on public verification, read once here: a value the host cannot read stops it at
        // startup (docs/adr/0029).
        services.AddUserRateLimit(CertificationVerificationLimits.From(configuration));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/certification").MapModuleEndpoints<CertificationModule>();
}
