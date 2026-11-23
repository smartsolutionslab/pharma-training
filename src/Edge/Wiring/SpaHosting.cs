using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using SmartSolutionsLab.PharmaTraining.Edge;
using Yarp.ReverseProxy.Model;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Published, the edge serves the web app's build from its web root (docs/adr/0005). Only a page load, a GET or HEAD with
/// Sec-Fetch-Mode: navigate or whose Accept lists text/html, answers index.html (no-cache), and only for a path that is no
/// file, not under /api and none of the edge's own: the app's pages share their paths with the modules' APIs (/catalog),
/// so it is answered before the module's route, while a call for data goes on to the service. Files are served to anyone,
/// /assets/* (named by their content) cached for good. Without index.html in the web root, as in run mode where the dev
/// server serves the app, nothing is added. Placed after UseApiPrefix, which routes the request. index.html is answered
/// with its <see cref="ContentSecurityPolicy"/>, built once from the file as it is read here: its inline scripts' hashes,
/// and calls to the identity provider's origin (Authentication:Authority) and blob storage's (Edge:BlobEndpoint, published).
/// </summary>
public static class SpaHosting
{
    private const string IndexFile = "index.html";

    /// <summary>The build names these files by their content, so a new build never reuses a name.</summary>
    private const string AssetsPath = "/assets";

    /// <summary>A year, the longest max-age caches are asked to honour.</summary>
    private const string AssetCaching = "public, max-age=31536000, immutable";

    /// <summary>The health probes' paths, never a page of the app even where they are not mapped.</summary>
    private static readonly PathString[] ProbePaths = ["/health", "/alive"];

    /// <summary>The addresses whose origins the page may call besides the edge: the identity provider and, published, blob storage.</summary>
    private static readonly string[] ConnectAddresses = ["Authentication:Authority", "Edge:BlobEndpoint"];

    /// <summary>The addresses whose origins the page may play media from besides the edge and <c>blob:</c>: published, blob storage (#262).</summary>
    private static readonly string[] MediaAddresses = ["Edge:BlobEndpoint"];

    public static WebApplication UseSpaHosting(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        var index = app.Environment.WebRootFileProvider.GetFileInfo(IndexFile);
        if (!index.Exists)
        {
            return app;
        }

        var policy = PolicyFor(index, app.Configuration);
        app.UseWhen(
            context => !context.Request.PathBase.HasValue,
            files => files.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = file =>
                {
                    if (file.Context.Request.Path.StartsWithSegments(AssetsPath))
                    {
                        file.Context.Response.Headers.CacheControl = AssetCaching;
                    }

                    // A page served as a file (/index.html) gets what the page fallback gives it: without the policy it could be
                    // framed and run injected script, and without no-cache a new deployment's app would not be loaded.
                    if (file.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        file.Context.Response.Headers.CacheControl = "no-cache";
                        file.Context.Response.Headers[HeaderNames.ContentSecurityPolicy] = policy;
                    }
                },
            }));

        app.Use(async (context, next) =>
        {
            if (!IsPageLoad(context.Request) || !IsAPageOfTheApp(context))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = index.Length;
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers[HeaderNames.ContentSecurityPolicy] = policy;
            if (HttpMethods.IsGet(context.Request.Method))
            {
                await context.Response.SendFileAsync(index, context.RequestAborted);
            }
        });

        return app;
    }

    private static string PolicyFor(IFileInfo index, IConfiguration configuration)
    {
        using var reader = new StreamReader(index.CreateReadStream(), System.Text.Encoding.UTF8);
        var page = reader.ReadToEnd();
        return ContentSecurityPolicy.For(page, OriginsOf(ConnectAddresses, configuration), OriginsOf(MediaAddresses, configuration));
    }

    private static IEnumerable<string> OriginsOf(IEnumerable<string> keys, IConfiguration configuration) =>
        keys
            .Select(key => configuration[key])
            .OfType<string>()
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(ContentSecurityPolicy.OriginOf);

    /// <summary>What a browser sends when it loads a page; a fetch for data asks for JSON, a form post is no GET.</summary>
    private static bool IsPageLoad(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        return string.Equals(request.Headers["Sec-Fetch-Mode"], "navigate", StringComparison.OrdinalIgnoreCase)
            || request.GetTypedHeaders().Accept.Any(type => type.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A path the edge maps nothing of its own to (a module's route aside), outside /api, and no file.</summary>
    private static bool IsAPageOfTheApp(HttpContext context)
    {
        var request = context.Request;
        if (request.PathBase.HasValue || ProbePaths.Any(probe => request.Path.StartsWithSegments(probe)))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        if (path[(path.LastIndexOf('/') + 1)..].Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        return endpoint is null || endpoint.Metadata.GetMetadata<RouteModel>() is not null;
    }
}
