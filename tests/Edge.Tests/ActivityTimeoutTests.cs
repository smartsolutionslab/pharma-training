using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// What a caller is answered when the edge's wait outlasts YARP's activity timeout (docs/adr/0021, #260): a signed-in
/// caller's body of unknown length that stops arriving while the edge holds it, and a service that never answers the
/// edge's <c>Expect: 100-continue</c>. YARP's own forwarder runs the edge's transforms and forwarder client, with an
/// activity timeout of 200 ms in place of the 100 s default.
/// </summary>
public class ActivityTimeoutTests
{
    private static readonly TimeSpan ActivityTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task A_signed_in_callers_body_that_stalls_while_the_edge_holds_it_is_answered_gateway_timeout()
    {
        using var service = SilentService.Start();
        using var edge = new Edge();
        var context = Caller(new StalledStream(), signedIn: true);
        context.Request.Headers.TransferEncoding = "chunked";

        var error = await edge.ForwardAsync(context, service.Address);

        (error, context.Response.StatusCode).ShouldBe((ForwarderError.RequestTimedOut, StatusCodes.Status504GatewayTimeout));
        service.Received.ShouldBeEmpty("The edge forwarded the request; the timeout was not the hold's.");
    }

    [Fact]
    public async Task A_service_that_never_answers_the_edges_expect_continue_is_answered_gateway_timeout()
    {
        using var service = SilentService.Start();
        using var edge = new Edge();
        var context = Caller(new MemoryStream(new byte[4_096]), signedIn: true);
        context.Request.ContentLength = 4_096;

        var error = await edge.ForwardAsync(context, service.Address);

        (error, context.Response.StatusCode).ShouldBe((ForwarderError.RequestTimedOut, StatusCodes.Status504GatewayTimeout));
        service.Received.ShouldContain("Expect: 100-continue", Case.Insensitive);
        service.Received.ShouldEndWith("\r\n\r\n", customMessage: "The edge sent body bytes; it was not waiting for 100 Continue.");
    }

    private static DefaultHttpContext Caller(Stream body, bool signedIn)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestWithBody());
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/compliance/pharmacies/1/assignments";
        context.Request.ContentType = "application/json";
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", "developer")], "Bearer"));
        }

        return context;
    }

    /// <summary>The edge's forwarding as <c>AddModuleRoutes</c> configures it, for one route, with a short activity timeout.</summary>
    private sealed class Edge : IDisposable
    {
        private readonly ServiceProvider services;

        private readonly HttpMessageInvoker client;

        public Edge()
        {
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            collection.AddReverseProxy().ForwardBodiesBehindContinue();
            services = collection.BuildServiceProvider();
            client = services.GetRequiredService<IForwarderHttpClientFactory>()
                .CreateClient(new ForwarderHttpClientContext { ClusterId = "compliance", NewConfig = HttpClientConfig.Empty });
        }

        public async Task<ForwarderError> ForwardAsync(HttpContext context, string destination)
        {
            var transformer = services.GetRequiredService<ITransformBuilder>()
                .Build(new RouteConfig { RouteId = "compliance", ClusterId = "compliance" }, new ClusterConfig { ClusterId = "compliance" });
            var forwarding = services.GetRequiredService<IHttpForwarder>()
                .SendAsync(context, destination, client, new ForwarderRequestConfig { ActivityTimeout = ActivityTimeout }, transformer)
                .AsTask();

            // A forwarder that ignored its activity timeout would hang here; the test's own token ends it instead.
            return await forwarding.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            client.Dispose();
            services.Dispose();
        }
    }
}
