using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Documents;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests;

public class CertificationModuleTests
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

        new CertificationModule().AddServices(services, configuration);
        var buildContainer = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose();

        Should.NotThrow(buildContainer);
    }

    /// <summary>
    /// The consumer that renders a certificate's PDF takes the renderer, the create-only writes into the module's own
    /// container and the links into the web app, for the verification link the PDF prints (#50).
    /// </summary>
    [Fact]
    public void The_module_registers_the_pdf_renderer_the_blob_writes_and_the_link_builder()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
            .Build();

        new CertificationModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICertificateRenderer>().ShouldBeOfType<CertificatePdfRenderer>();
        provider.GetRequiredService<IBlobWrites>().ShouldBeOfType<AzureBlobWrites>();
        provider.GetRequiredService<IFrontendLinks>().ShouldBeOfType<FrontendLinks>();
    }
}
