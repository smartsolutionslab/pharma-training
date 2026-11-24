using System.Collections;
using Microsoft.AspNetCore.Http;

namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Telemetry;

/// <summary>
/// The scope every log of a request carries, in place of ASP.NET Core hosting's own (#311): <c>RequestId</c>, and
/// <c>RequestPath</c> as the route's template (<see cref="RequestPaths"/>), never the path itself. The template is known
/// only once routing chose the route, so it is read when a log reads the scope, and kept once the request ends, for an
/// exporter that reads scopes later.
/// </summary>
public sealed class RequestLogScope(HttpContext context) : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly string requestId = context.TraceIdentifier;

    private string? finalPath;

    public int Count => 2;

    public KeyValuePair<string, object?> this[int index]
    {
        get
        {
            Ensure.That(index).InRange(0, Count - 1);
            return index == 0 ? new("RequestId", requestId) : new("RequestPath", Path());
        }
    }

    /// <summary>Keeps the path as it is when the request ends; the context may serve another request afterwards.</summary>
    public void End() => finalPath = Path();

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"RequestPath:{Path()} RequestId:{requestId}";

    private string Path() => finalPath ?? RequestPaths.Template(context) ?? RequestPaths.FirstSegment(context.Request.Path.Value);
}
