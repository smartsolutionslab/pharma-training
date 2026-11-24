using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Telemetry;

/// <summary>
/// What telemetry may say of a request's path (#311): its route's template, which names a value rather than holding it,
/// or, for a request no route took, its first segment alone; a path may carry a credential (Certification's verification
/// code) in any later one.
/// </summary>
public static class RequestPaths
{
    /// <summary>The template of the route that took the request, or null before routing or where none did.</summary>
    public static string? Template(HttpContext context)
    {
        Ensure.That(context).IsNotNull();

        return context.GetEndpoint() is RouteEndpoint route ? route.RoutePattern.RawText : null;
    }

    /// <summary>The path's first segment, followed by <c>/…</c> where more came after it: <c>/catalog/…</c>.</summary>
    public static string FirstSegment(string? path) => Leading(path, 1);

    /// <summary>The path's first <paramref name="segments"/> segments, followed by <c>/…</c> where more came after them.</summary>
    public static string Leading(string? path, int segments)
    {
        Ensure.That(segments).AtLeast(1);

        var parts = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var kept = "/" + string.Join('/', parts.Take(segments));
        return parts.Length > segments ? kept + "/…" : kept;
    }
}
