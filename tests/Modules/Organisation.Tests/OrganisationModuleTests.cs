using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests;

public class OrganisationModuleTests
{
    /// <summary>Starts from what every host provides (logging), adds the module, and validates every registration.</summary>
    [Fact]
    public void The_module_can_resolve_every_service_it_registers()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
            .Build();

        // The host registers the project's IChatClient; a module's test stands in for it and never calls a real model.
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));

        new OrganisationModule().AddServices(services, configuration);
        var buildContainer = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose();

        Should.NotThrow(buildContainer);
    }

    [Fact]
    public void The_module_registers_its_invitation_mail_limits()
    {
        using var provider = Provider();

        provider.GetService<InvitationMailLimits>().ShouldBe(InvitationMailLimits.Default);
    }

    /// <summary>Organisation mails its invitations, so it registers the mail sender and the links into the web app itself.</summary>
    [Fact]
    public void The_module_registers_the_mail_sender_and_link_builder()
    {
        var services = new ServiceCollection().AddLogging();
        new OrganisationModule().AddServices(services, Configuration());

        services.ShouldContain(service => service.ServiceType == typeof(IMailSender), "Organisation registers no IMailSender");
        services.ShouldContain(service => service.ServiceType == typeof(IFrontendLinks), "Organisation registers no IFrontendLinks");
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
        .Build();

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        new OrganisationModule().AddServices(services, Configuration());
        return services.BuildServiceProvider();
    }
}
