using Yarp.ReverseProxy.Forwarder;

namespace SmartSolutionsLab.PharmaTraining.Edge.Forwarding;

/// <summary>
/// The edge's forwarder client, which waits for a service's <c>100 Continue</c> or final answer however long it takes
/// before it sends a body (#214, docs/adr/0019). With SocketsHttpHandler's default of one second, a service slower to
/// answer than that (a cold key fetch before authorization) would get the body anyway, and a refusal could reset it
/// again. Every service is Kestrel, which sends <c>100 Continue</c> as soon as the endpoint reads the body, so the
/// timeout's fallback is never needed; YARP's activity timeout still bounds the wait.
/// </summary>
public sealed class ContinueWaitingForwarderHttpClientFactory(ILogger<ForwarderHttpClientFactory> logger) : ForwarderHttpClientFactory(logger)
{
    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        Ensure.That(handler).IsNotNull();

        base.ConfigureHandler(context, handler);
        handler.Expect100ContinueTimeout = Timeout.InfiniteTimeSpan;
    }
}
