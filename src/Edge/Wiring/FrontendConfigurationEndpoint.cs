using Microsoft.AspNetCore.Http;
using SmartSolutionsLab.PharmaTraining.Edge;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// GET /config.json (and /api/config.json): the realm the browser signs in with, from the edge's own
/// Authentication:Authority, the web app's client, and the deployment from Edge:Environment, which the AppHost sets in
/// every mode (the -e name published). Anonymous, since the browser reads it before it signs in; it holds nothing a
/// discovery document or the address does not tell anyway.
/// </summary>
public static class FrontendConfigurationEndpoint
{
    /// <summary>The web app's client in the realm (src/AppHost/realms).</summary>
    public const string WebClientId = "pharma-training-web";

    /// <summary>What the edge reports without Edge:Environment, as outside the AppHost: never staging, so the demo seeder refuses it.</summary>
    public const string UnknownEnvironment = "unknown";

    public static WebApplication MapFrontendConfiguration(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        app.MapGet("/config.json", (IConfiguration configuration) => TypedResults.Ok(new FrontendConfiguration(
                configuration["Authentication:Authority"] ?? throw new InvalidOperationException("Authentication:Authority is missing; the AppHost provides it."),
                WebClientId,
                configuration["Edge:Environment"] is { Length: > 0 } environment ? environment : UnknownEnvironment)))
            .AllowAnonymous();
        return app;
    }
}
