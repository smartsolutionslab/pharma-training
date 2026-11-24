using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests;

public class EditorialModuleTests
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

        new EditorialModule().AddServices(services, configuration);
        var buildContainer = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose();

        Should.NotThrow(buildContainer);
    }

    /// <summary>The host reads four eyes once, at startup, and every publish takes the same settings.</summary>
    [Fact]
    public void The_module_registers_the_review_settings_once()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost", ["Editorial:FourEyes"] = "true" })
            .Build();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));

        new EditorialModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();

        services.Count(service => service.ServiceType == typeof(ReviewSettings)).ShouldBe(1);
        provider.GetRequiredService<ReviewSettings>().Policy.ShouldBe(ReviewPolicy.FourEyes);
    }

    /// <summary>The PDF text extractor holds no state: one instance, registered once, reads every recorded PDF (#157).</summary>
    [Fact]
    public void The_module_registers_pdfpig_as_its_pdf_text_extractor_once_as_a_singleton()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
            .Build();
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));

        new EditorialModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();

        var registration = services.Where(service => service.ServiceType == typeof(IPdfTextExtractor)).ShouldHaveSingleItem();
        registration.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        provider.GetRequiredService<IPdfTextExtractor>().ShouldBeOfType<PdfPigTextExtractor>();
    }
}
