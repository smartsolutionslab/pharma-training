using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Edge.Forwarding;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The edge forwards every request body behind <c>Expect: 100-continue</c> (#214, docs/adr/0021). A service that
/// refuses a body as too large answers <c>413</c> before it asks for the body; the edge then never sends it, so no body
/// is in flight when the service closes the connection, and the <c>413</c> reaches the caller instead of the edge's
/// <c>502</c>. A signed-in caller's body of unknown length is read at the edge first, up to
/// <c>Edge:MaxBufferedRequestBodyBytes</c> (at most Kestrel's <c>MaxRequestBodySize</c>), and forwarded with its length, since a service can refuse a chunked body
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
            .Validate<IOptions<KestrelServerOptions>>(
                (limits, kestrel) => limits.MaxBufferedRequestBodyBytes <= LargestLimit(kestrel.Value),
                "Edge:MaxBufferedRequestBodyBytes must not exceed Kestrel's MaxRequestBodySize (30 000 000 bytes unless Kestrel:Limits:MaxRequestBodySize lowers it): Kestrel refuses a larger body first.")
            .ValidateOnStart();
        proxy.Services.TryAddSingleton<ContinueExpectingRequestTransform>();
        proxy.Services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory, ContinueWaitingForwarderHttpClientFactory>());
        proxy.AddTransforms(builder => builder.RequestTransforms.Add(builder.Services.GetRequiredService<ContinueExpectingRequestTransform>()));

        return proxy;
    }

    // A limit above Kestrel's own is never reached, since Kestrel answers 413 first. Without one, the edge still reads
    // one byte past its limit, so long.MaxValue itself would overflow.
    private static long LargestLimit(KestrelServerOptions kestrel) => kestrel.Limits.MaxRequestBodySize ?? long.MaxValue - 1;
}
