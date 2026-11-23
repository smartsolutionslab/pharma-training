using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// An endpoint names a registered limit with <c>RequireUserRateLimit</c>: a caller over it gets 429 with
/// <c>Retry-After</c> and the limit's code, and the endpoint's delegate, and so the model, is not reached. Every admitted
/// request counts, whatever the endpoint answers. The request delegate is invoked directly, with authorization already
/// passed, as the filter runs after it.
/// </summary>
public sealed class UserRateLimitEndpointTests : IAsyncDisposable
{
    private const string Ask = "/ask";
    private const string Refuse = "/refuse";
    private const string LimitName = "test.ask";

    private static readonly string Caller = Guid.CreateVersion7().ToString();

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChatClient model = new("Antwort");
    private readonly WebApplication app;

    public UserRateLimitEndpointTests()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton<IChatClient>(model);
        builder.Services.AddUserRateLimit(UserRateLimit.Of(LimitName, new TestLimitReached(), UserRateWindow.Of(2, TimeSpan.FromMinutes(1))));
        app = builder.Build();
        app.MapPost(Ask, async (IChatClient chat, CancellationToken cancellationToken) =>
        {
            await chat.GetResponseAsync("Frage", cancellationToken: cancellationToken);
            return TypedResults.Ok();
        }).RequireUserRateLimit(LimitName);
        app.MapPost(Refuse, () => TypedResults.NotFound()).RequireUserRateLimit(LimitName);
    }

    [Fact]
    public async Task Over_the_limit_answers_429_with_retry_after_and_the_limits_code()
    {
        await PostAsync(Ask, Caller);
        await PostAsync(Ask, Caller);

        var refused = await PostAsync(Ask, Caller);

        refused.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        refused.RetryAfter.ShouldBe("60");
        refused.Code.ShouldBe("test.limit-reached");
        refused.Title.ShouldBe(new TestLimitReached().Message);
    }

    [Fact]
    public async Task Over_the_limit_the_model_is_not_called()
    {
        for (var request = 0; request < 3; request++)
        {
            await PostAsync(Ask, Caller);
        }

        model.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Retry_after_is_rounded_up_to_whole_seconds()
    {
        await PostAsync(Ask, Caller);
        await PostAsync(Ask, Caller);
        time.Advance(TimeSpan.FromMilliseconds(59_500));

        var refused = await PostAsync(Ask, Caller);

        refused.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        refused.RetryAfter.ShouldBe("1");
    }

    [Fact]
    public async Task Another_caller_is_still_admitted()
    {
        await PostAsync(Ask, Caller);
        await PostAsync(Ask, Caller);
        (await PostAsync(Ask, Caller)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);

        var other = await PostAsync(Ask, Guid.CreateVersion7().ToString());

        other.Status.ShouldBe((int)HttpStatusCode.OK);
        other.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task A_request_the_endpoint_refuses_still_counts()
    {
        var statuses = new List<int>();
        for (var request = 0; request < 3; request++)
        {
            statuses.Add((await PostAsync(Refuse, Caller)).Status);
        }

        statuses.ShouldBe([(int)HttpStatusCode.NotFound, (int)HttpStatusCode.NotFound, (int)HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task After_the_window_the_caller_is_admitted_again()
    {
        await PostAsync(Ask, Caller);
        await PostAsync(Ask, Caller);
        (await PostAsync(Ask, Caller)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        time.Advance(TimeSpan.FromMinutes(1));

        var admitted = await PostAsync(Ask, Caller);

        admitted.Status.ShouldBe((int)HttpStatusCode.OK);
        model.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task An_endpoint_naming_an_unregistered_limit_fails_when_its_endpoints_are_built()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddUserRateLimit(UserRateLimit.Of(LimitName, new TestLimitReached(), UserRateWindow.Of(2, TimeSpan.FromMinutes(1))));
        await using var misnamed = builder.Build();
        misnamed.MapPost("/typo", () => TypedResults.Ok()).RequireUserRateLimit("test.aks");

        var build = () => ((IEndpointRouteBuilder)misnamed).DataSources.SelectMany(source => source.Endpoints).ToList();

        Should.Throw<InvalidOperationException>(build).Message.ShouldContain("test.aks");
    }

    [Fact]
    public void The_endpoint_declares_a_429_problem_response()
    {
        var endpoint = Endpoint(Ask);

        endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ShouldContain(produces => produces.StatusCode == StatusCodes.Status429TooManyRequests);
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        model.Dispose();
    }

    private RouteEndpoint Endpoint(string route) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);

    private async Task<Answer> PostAsync(string route, string subject)
    {
        await using var scope = app.Services.CreateAsyncScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], authenticationType: "Bearer")),
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = route;
        context.Response.Body = body;

        await Endpoint(route).RequestDelegate!(context);

        var retryAfter = context.Response.Headers.RetryAfter;
        return new Answer(
            context.Response.StatusCode,
            retryAfter.Count == 0 ? null : retryAfter.ToString(),
            Property(body, "code"),
            Property(body, "title"));
    }

    private static string? Property(MemoryStream body, string name)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body.ToArray());
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private sealed record Answer(int Status, string? RetryAfter, string? Code, string? Title);
}
