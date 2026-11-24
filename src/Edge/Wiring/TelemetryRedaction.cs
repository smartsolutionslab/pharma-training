using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using SmartSolutionsLab.PharmaTraining.ServiceDefaults.Telemetry;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The edge's spans record no path beyond the module (#311, docs/adr/0033): the edge knows no module's routes, so it
/// cannot tell a path that carries a credential (Certification's verification code) from any other. Its server span's
/// <c>url.path</c> keeps <c>/api</c> where the call came under it, and the module (<c>/api/certification/…</c>); the span
/// of every call it makes keeps the module alone in <c>url.full</c> (<c>http://certification/certification/…</c>). The
/// service's own span, in the same trace, records its route's template.
/// </summary>
public static class TelemetryRedaction
{
    private const string ApiSegment = "api";

    public static IServiceCollection AddPathRedactedTelemetry(this IServiceCollection services)
    {
        Ensure.That(services).IsNotNull();

        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.EnrichWithHttpRequest = (activity, request) => activity.SetTag("url.path", ModuleOf(request.Path.Value)));
        services.Configure<HttpClientTraceInstrumentationOptions>(options =>
            options.EnrichWithHttpRequestMessage = (activity, message) =>
            {
                if (message.RequestUri is { IsAbsoluteUri: true } uri)
                {
                    activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Authority) + RequestPaths.FirstSegment(uri.AbsolutePath));
                }
            });
        return services;
    }

    /// <summary>The path up to the module: <c>/api/certification/…</c> under the web app's prefix, else <c>/certification/…</c>.</summary>
    private static string ModuleOf(string? path)
    {
        var first = (path ?? string.Empty).TrimStart('/').Split('/')[0];
        return RequestPaths.Leading(path, string.Equals(first, ApiSegment, StringComparison.Ordinal) ? 2 : 1);
    }
}
