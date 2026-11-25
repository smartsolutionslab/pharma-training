namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Sends the run's requests through the caller's handler, which it never disposes, and remembers the last one by its
/// method and path alone (no query, header or body, where a credential could be), so a failure the run did not expect
/// names the step it happened in.
/// </summary>
internal sealed class StepTracker(HttpMessageHandler handler) : HttpMessageHandler
{
    private readonly HttpMessageInvoker invoker = new(handler, disposeHandler: false);

    /// <summary>The last request sent, such as <c>POST /api/organisation/pharmacies</c>.</summary>
    public string Current { get; private set; } = "the start";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Current = $"{request.Method} {request.RequestUri?.AbsolutePath}";
        return invoker.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            invoker.Dispose();
        }

        base.Dispose(disposing);
    }
}
