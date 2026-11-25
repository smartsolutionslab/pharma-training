using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// How the edge readies a caller's body before it forwards it to a service (docs/adr/0021), through the transforms
/// <c>ForwardBodiesBehindContinue</c> gives every route. A body of unknown length is held at the edge only for a
/// caller the edge has signed in, and only up to <c>Edge:MaxBufferedRequestBodyBytes</c>; an anonymous caller's body
/// is streamed as it comes, so it costs the edge neither memory nor disk.
/// </summary>
public class ForwardedBodiesTests
{
    private const int Mebibyte = 1_048_576;

    // Kestrel's MaxRequestBodySize unless something lowers it.
    private const long KestrelDefaultLimit = 30_000_000;

    [Fact]
    public async Task An_anonymous_callers_body_of_unknown_length_is_streamed_without_being_read_at_the_edge()
    {
        using var proxy = new Proxy();
        var body = new ReadCountingStream(10 * Mebibyte);
        var context = Caller(body, signedIn: false);
        using var forwarded = Forwarded();

        await proxy.TransformAsync(context, forwarded);

        body.BytesRead.ShouldBe(0);
        context.Request.Body.ShouldBeSameAs(body);
        forwarded.Content!.Headers.ContentLength.ShouldBeNull();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_signed_in_callers_body_of_unknown_length_is_held_and_forwarded_with_its_length_behind_continue()
    {
        using var proxy = new Proxy();
        var body = new ReadCountingStream(200_000);
        var context = Caller(body, signedIn: true);
        using var forwarded = Forwarded();

        await proxy.TransformAsync(context, forwarded);

        forwarded.Content!.Headers.ContentLength.ShouldBe(200_000);
        forwarded.Headers.ExpectContinue.ShouldBe(true);
        context.Request.Body.Position.ShouldBe(0);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_signed_in_callers_body_of_unknown_length_above_the_edges_limit_is_refused_as_too_large_without_reading_it_whole()
    {
        using var proxy = new Proxy(limit: 1_024);
        var body = new ReadCountingStream(Mebibyte);
        var context = Caller(body, signedIn: true);
        using var forwarded = Forwarded();

        await proxy.TransformAsync(context, forwarded);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        body.BytesRead.ShouldBeLessThan(Mebibyte);
    }

    [Theory]
    [InlineData(Mebibyte, StatusCodes.Status200OK)]
    [InlineData(Mebibyte + 1, StatusCodes.Status413PayloadTooLarge)]
    public async Task The_edge_holds_a_body_of_unknown_length_of_up_to_one_mebibyte_by_default(int length, int status)
    {
        using var proxy = new Proxy();
        var context = Caller(new ReadCountingStream(length), signedIn: true);
        using var forwarded = Forwarded();

        await proxy.TransformAsync(context, forwarded);

        context.Response.StatusCode.ShouldBe(status);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    public async Task A_body_kestrel_refuses_while_the_edge_reads_it_is_answered_with_kestrels_status_and_not_forwarded(int status)
    {
        using var proxy = new Proxy();
        var refusal = new BadHttpRequestException("Kestrel refused the body.", status);
        var context = Caller(new ReadCountingStream(Mebibyte, refusal), signedIn: true);
        using var forwarded = Forwarded();

        await proxy.TransformAsync(context, forwarded);

        context.Response.StatusCode.ShouldBe(status);
    }

    [Fact]
    public async Task A_body_with_a_length_is_forwarded_behind_continue_without_being_read_at_the_edge()
    {
        using var proxy = new Proxy();
        var body = new ReadCountingStream(10 * Mebibyte);
        var context = Caller(body, signedIn: false);
        context.Request.ContentLength = 10 * Mebibyte;
        using var forwarded = Forwarded();

        await proxy.TransformAsync(context, forwarded);

        body.BytesRead.ShouldBe(0);
        forwarded.Headers.ExpectContinue.ShouldBe(true);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task A_limit_that_is_not_positive_stops_the_edge_at_startup(string limit)
    {
        using var proxy = new Proxy(limit);

        await Should.ThrowAsync<OptionsValidationException>(proxy.ValidateOnStartAsync);
    }

    [Fact]
    public async Task A_limit_above_kestrels_request_body_limit_stops_the_edge_at_startup_naming_the_setting()
    {
        using var proxy = new Proxy(KestrelDefaultLimit + 1);

        var refusal = await Should.ThrowAsync<OptionsValidationException>(proxy.ValidateOnStartAsync);

        refusal.Message.ShouldContain("Edge:MaxBufferedRequestBodyBytes");
    }

    [Fact]
    public async Task A_limit_above_a_lowered_kestrel_request_body_limit_stops_the_edge_at_startup()
    {
        using var proxy = new Proxy(2_000_001, kestrelLimit: 2_000_000);

        var refusal = await Should.ThrowAsync<OptionsValidationException>(proxy.ValidateOnStartAsync);

        refusal.Message.ShouldContain("Edge:MaxBufferedRequestBodyBytes");
    }

    [Fact]
    public async Task The_largest_limit_without_a_kestrel_request_body_limit_stops_the_edge_at_startup()
    {
        using var proxy = new Proxy(long.MaxValue, kestrelLimit: null);

        var refusal = await Should.ThrowAsync<OptionsValidationException>(proxy.ValidateOnStartAsync);

        refusal.Message.ShouldContain("Edge:MaxBufferedRequestBodyBytes");
    }

    [Fact]
    public async Task A_limit_equal_to_kestrels_request_body_limit_starts_the_edge()
    {
        using var proxy = new Proxy(KestrelDefaultLimit);

        await Should.NotThrowAsync(proxy.ValidateOnStartAsync);
    }

    private static DefaultHttpContext Caller(Stream body, bool signedIn)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/compliance/pharmacies/1/assignments";
        context.Request.ContentType = "application/json";
        context.Request.Body = body;
        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", "developer")], "Bearer"));
        }

        return context;
    }

    // What YARP hands the transforms: the forwarded request, whose content streams the caller's body when it is sent.
    private static HttpRequestMessage Forwarded() =>
        new(HttpMethod.Post, new Uri("http://compliance/compliance/pharmacies/1/assignments")) { Content = new StreamContent(new ReadCountingStream(0)) };

    /// <summary>The edge's proxy services, as <c>AddModuleRoutes</c> configures them, with the transforms of one route.</summary>
    private sealed class Proxy : IDisposable
    {
        private readonly ServiceProvider services;

        public Proxy(long limit)
            : this(limit.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
        }

        /// <summary>The proxy services under a Kestrel whose <c>MaxRequestBodySize</c> is <paramref name="kestrelLimit"/> (null: none).</summary>
        public Proxy(long limit, long? kestrelLimit)
            : this(limit.ToString(System.Globalization.CultureInfo.InvariantCulture), kestrel => kestrel.Limits.MaxRequestBodySize = kestrelLimit)
        {
        }

        public Proxy(string? limit = null, Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>? kestrel = null)
        {
            Dictionary<string, string?> settings = [];
            if (limit is not null)
            {
                settings["Edge:MaxBufferedRequestBodyBytes"] = limit;
            }

            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            if (kestrel is not null)
            {
                collection.Configure(kestrel);
            }

            collection.AddReverseProxy().ForwardBodiesBehindContinue();
            services = collection.BuildServiceProvider();
        }

        public async Task TransformAsync(HttpContext context, HttpRequestMessage forwarded)
        {
            var transformer = services.GetRequiredService<ITransformBuilder>()
                .Build(new RouteConfig { RouteId = "compliance", ClusterId = "compliance" }, new ClusterConfig { ClusterId = "compliance" });

            await transformer.TransformRequestAsync(context, forwarded, "http://compliance", TestContext.Current.CancellationToken);
        }

        public async Task ValidateOnStartAsync() =>
            await services.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken);

        public void Dispose() => services.Dispose();
    }
}
