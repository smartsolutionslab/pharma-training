using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

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

    [Fact]
    public void The_module_registers_its_recipient_and_sender_mail_limits()
    {
        using var provider = Provider();

        provider.GetService<RecipientAndSenderMailLimits>().ShouldBe(RecipientAndSenderMailLimits.Default);
    }

    [Fact]
    public void The_module_registers_its_invitation_recipient_and_sender_stores()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IInvitationRecipientStore>().ShouldNotBeNull("the module registered no store of invitation recipients");
        scope.ServiceProvider.GetService<IInvitationSenderStore>().ShouldNotBeNull("the module registered no store of invitation senders");
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

    /// <summary>#115 slice 6: Organisation deletes an erased person's Keycloak account through the admin API, with the settings the AppHost passes.</summary>
    [Fact]
    public void The_module_registers_keycloak_accounts()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:database"] = "Host=localhost",
                ["Keycloak:Accounts:Address"] = "http://keycloak.internal:8080",
                ["Keycloak:Accounts:Realm"] = "pharma-training",
                ["Keycloak:Accounts:ClientId"] = "pharma-training-erasure",
                ["Keycloak:Accounts:ClientSecret"] = "erasure-client-secret-0123456789abcdef",
            })
            .Build();
        new OrganisationModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IAccounts>().ShouldBeOfType<KeycloakAccounts>("the module registered no IAccounts over Keycloak");
    }

    /// <summary>
    /// #458 review: a host without <c>Keycloak:Accounts</c> still builds the erasure consumers, which take IAccounts, and
    /// the first account read or deletion fails naming the missing setting, instead of every erasure failing to resolve
    /// its consumer without saying why.
    /// </summary>
    [Fact]
    public async Task Without_keycloak_settings_the_first_account_call_fails_naming_the_missing_setting()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        new OrganisationModule().AddServices(services, Configuration());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var accounts = scope.ServiceProvider.GetService<IAccounts>();

        accounts.ShouldNotBeNull("the erasure consumers take IAccounts, so a host without the settings could not build them");
        var refused = await Should.ThrowAsync<OptionsValidationException>(() => accounts.FindAsync(PersonId.From(Guid.CreateVersion7()), TestContext.Current.CancellationToken));
        refused.Message.ShouldContain("Keycloak:Accounts:");
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
