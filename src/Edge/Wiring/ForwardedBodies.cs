using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSolutionsLab.PharmaTraining.Edge.Forwarding;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The edge forwards every request body behind <c>Expect: 100-continue</c> (#214, docs/adr/0019). A service that
/// refuses a body as too large answers <c>413</c> before it asks for the body; the edge then never sends it, so no body
/// is in flight when the service closes the connection, and the <c>413</c> reaches the caller instead of the edge's
/// <c>502</c>. A body of unknown length is read whole at the edge first and forwarded with its length, since a service
/// can refuse a chunked body only partway through it.
/// </summary>
public static class ForwardedBodies
{
    public static IReverseProxyBuilder ForwardBodiesBehindContinue(this IReverseProxyBuilder proxy)
    {
        Ensure.That(proxy).IsNotNull();

        proxy.Services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory, ContinueWaitingForwarderHttpClientFactory>());
        proxy.AddTransforms(builder => builder.AddRequestTransform(ExpectContinueAsync));

        return proxy;
    }

    private static async ValueTask ExpectContinueAsync(RequestTransformContext context)
    {
        if (context.ProxyRequest.Content is null)
        {
            return;
        }

        var request = context.HttpContext.Request;
        if (request.ContentLength is null)
        {
            request.EnableBuffering();
            await request.Body.DrainAsync(context.CancellationToken);
            request.Body.Position = 0;
            context.ProxyRequest.Content.Headers.ContentLength = request.Body.Length;
        }

        context.ProxyRequest.Headers.ExpectContinue = true;
    }
}
