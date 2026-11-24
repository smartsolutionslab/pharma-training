using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

public sealed class EditorialModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleInfrastructure<EditorialModule>(configuration);

        // Read once, here: a value the host cannot read stops it at startup, never on a publish (ADR 0007).
        services.AddSingleton(EditorialSettings.ReviewFrom(configuration));

        // Reads a recorded PDF's page text: one bounded extractor over PdfPig, so its slots bound the whole host (#316),
        // with its limit and deadline read once here like the line above. Convention registers only the stores taking the
        // DbContext.
        services.AddSingleton(EditorialSettings.PdfTextFrom(configuration));
        services.AddSingleton<IPdfTextParser, PdfPigTextExtractor>();
        services.AddSingleton<IPdfTextExtractor, BoundedPdfTextExtractor>();

        // A chapter's PdfText is nullable; its enum's values in the OpenAPI document are not.
        EditorialPdfTextSchema.Describe(services);

        // The per-editor limits on the endpoints that call the model, read once here like the line above (docs/adr/0020).
        foreach (var limit in EditorialModelLimits.From(configuration))
        {
            services.AddUserRateLimit(limit);
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/editorial").MapModuleEndpoints<EditorialModule>();
}
