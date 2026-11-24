using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

public sealed class OrganisationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<OrganisationModule>(configuration);

        // Organisation mails its invitations, so it registers the mail sender and the links into the web app itself;
        // its host says so with <SendsMail>, from which the AppHost gives it the mail settings (docs/adr/0004).
        services.AddModuleMail(configuration);
        services.AddSingleton(OrganisationLimits.InvitationMailsFrom(configuration));

        // The per-user limit on accepting invitations, read once here like the line above (docs/adr/0004, docs/adr/0020).
        services.AddUserRateLimit(OrganisationLimits.AcceptFrom(configuration));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/organisation").MapModuleEndpoints<OrganisationModule>();
}
