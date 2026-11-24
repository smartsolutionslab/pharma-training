using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSolutionsLab.PharmaTraining.Edge.Forwarding;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The edge forwards every request body behind <c>Expect: 100-continue</c> (#214, docs/adr/0021). A service that
/// refuses a body as too large answers <c>413</c> before it asks for the body; the edge then never sends it, so no body
/// is in flight when the service closes the connection, and the <c>413</c> reaches the caller instead of the edge's
/// <c>502</c>. A signed-in caller's body of unknown length is read at the edge first, up to
/// <c>Edge:MaxBufferedRequestBodyBytes</c>, and forwarded with its length, since a service can refuse a chunked body
/// only partway through it (<see cref="ContinueExpectingRequestTransform"/>).
/// </summary>
public static class ForwardedBodies
{
    public static IReverseProxyBuilder ForwardBodiesBehindContinue(this IReverseProxyBuilder proxy)
    {
        Ensure.That(proxy).IsNotNull();

        proxy.Services.AddOptions<BufferedBodyOptions>()
            .BindConfiguration(BufferedBodyOptions.Section)
            .Validate(limits => limits.MaxBufferedRequestBodyBytes > 0, "Edge:MaxBufferedRequestBodyBytes must be a positive number of bytes.")
            .ValidateOnStart();
        proxy.Services.TryAddSingleton<ContinueExpectingRequestTransform>();
        proxy.Services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory, ContinueWaitingForwarderHttpClientFactory>());
        proxy.AddTransforms(builder => builder.RequestTransforms.Add(builder.Services.GetRequiredService<ContinueExpectingRequestTransform>()));

        return proxy;
    }
}
