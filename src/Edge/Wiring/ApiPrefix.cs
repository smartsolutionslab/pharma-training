namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// The web app calls the edge under /api, through the dev server's proxy and published alike. A request whose path starts
/// with the whole segment /api is handled as the same request without it, so /api/me is /me and /api/&lt;module&gt;/... reaches
/// the module's service as /&lt;module&gt;/...; the service never sees the prefix. A request without it is unchanged.
/// </summary>
public static class ApiPrefix
{
    /// <summary>The segment the web app's generated client puts before every path.</summary>
    public const string Segment = "/api";

    public static WebApplication UseApiPrefix(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        app.UsePathBase(Segment);
        // Routing must match the path without the prefix: WebApplication would otherwise route before UsePathBase.
        app.UseRouting();
        return app;
    }
}
