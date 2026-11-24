using Microsoft.AspNetCore.Http;
using SmartSolutionsLab.PharmaTraining.Edge;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// GET /config.json (and /api/config.json): the realm the browser signs in with, from the edge's own
/// Authentication:Authority, and the web app's client. Anonymous, since the browser reads it before it signs in; it holds
/// nothing a discovery document does not publish anyway.
/// </summary>
public static class FrontendConfigurationEndpoint
{
    /// <summary>The web app's client in the realm (src/AppHost/realms).</summary>
    public const string WebClientId = "pharma-training-web";

    public static WebApplication MapFrontendConfiguration(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        app.MapGet("/config.json", (IConfiguration configuration) => TypedResults.Ok(new FrontendConfiguration(
                configuration["Authentication:Authority"] ?? throw new InvalidOperationException("Authentication:Authority is missing; the AppHost provides it."),
                WebClientId)))
            .AllowAnonymous();
        return app;
    }
}
