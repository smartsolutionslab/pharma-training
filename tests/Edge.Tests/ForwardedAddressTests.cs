using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// The address a service sees for a caller (docs/adr/0029): the edge forwards the address it saw itself as
/// <c>X-Forwarded-For</c>, in place of whatever the caller sent, so a caller cannot choose the address an anonymous
/// endpoint's per-address limit counts it under. YARP's default <c>X-Forwarded-*</c> transform does this (action
/// <c>Set</c>); the test pins it on the routes <c>AddModuleRoutes</c> gives every module.
/// </summary>
public class ForwardedAddressTests
{
    private static readonly IPAddress Caller = IPAddress.Parse("198.51.100.7");

    [Fact]
    public async Task A_callers_own_x_forwarded_for_is_replaced_by_the_address_the_edge_saw()
    {
        var context = Request(Caller);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        using var forwarded = Forwarded();

        await TransformAsync(context, forwarded);

        forwarded.Headers.GetValues("X-Forwarded-For").ShouldBe(["198.51.100.7"]);
    }

    [Fact]
    public async Task A_caller_without_x_forwarded_for_is_forwarded_with_the_address_the_edge_saw()
    {
        var context = Request(Caller);
        using var forwarded = Forwarded();

        await TransformAsync(context, forwarded);

        forwarded.Headers.GetValues("X-Forwarded-For").ShouldBe(["198.51.100.7"]);
    }

    private static DefaultHttpContext Request(IPAddress remote)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/certification/verify/k7Qp2xV9mL4rT8wZ1nB6cY3hAbCdEfGh";
        context.Connection.RemoteIpAddress = remote;
        return context;
    }

    private static HttpRequestMessage Forwarded() =>
        new(HttpMethod.Get, new Uri("http://certification/certification/verify/k7Qp2xV9mL4rT8wZ1nB6cY3hAbCdEfGh"));

    /// <summary>The transforms of the certification route, as the edge's <c>AddModuleRoutes</c> configures them.</summary>
    private static async Task TransformAsync(HttpContext context, HttpRequestMessage forwarded)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Edge:Modules:0"] = "certification" })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<IConfiguration>(configuration);
        collection.AddServiceDiscovery();
        collection.AddModuleRoutes(configuration);
        await using var services = collection.BuildServiceProvider();

        var transformer = services.GetRequiredService<ITransformBuilder>()
            .Build(new RouteConfig { RouteId = "certification", ClusterId = "certification" }, new ClusterConfig { ClusterId = "certification" });

        await transformer.TransformRequestAsync(context, forwarded, "http://certification", TestContext.Current.CancellationToken);
    }
}
