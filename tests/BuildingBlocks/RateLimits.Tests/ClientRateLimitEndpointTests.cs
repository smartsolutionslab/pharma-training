using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>
/// An anonymous endpoint names a registered limit with <c>RequireClientRateLimit</c> (docs/adr/0029): it counts per
/// client address (<see cref="ClientRateSubject"/>), not per token, so a caller over it gets 429 with
/// <c>Retry-After</c> and the limit's code, and the endpoint's delegate is not invoked. The refusal is logged with the
/// limit's name, never the address, which is personal data. The request delegate is invoked directly, as the filter
/// runs after authorization.
/// </summary>
public sealed class ClientRateLimitEndpointTests : IAsyncDisposable
{
    private const string Verify = "/verify";
    private const string LimitName = "test.verify";

    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.9");
    private static readonly IPAddress OtherAddress = IPAddress.Parse("198.51.100.7");

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly WebApplication app;
    private int invoked;

    public ClientRateLimitEndpointTests()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.AddFakeLogging();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddUserRateLimit(UserRateLimit.Of(LimitName, new TestLimitReached(), UserRateWindow.Of(2, TimeSpan.FromMinutes(1))));
        app = builder.Build();
        app.MapGet(Verify, () =>
        {
            Interlocked.Increment(ref invoked);
            return TypedResults.Ok();
        }).AllowAnonymous().RequireClientRateLimit(LimitName);
    }

    [Fact]
    public async Task The_third_anonymous_request_from_one_address_answers_429_and_the_endpoint_does_not_run()
    {
        var first = await GetAsync(Address);
        var second = await GetAsync(Address);

        var third = await GetAsync(Address);

        first.Status.ShouldBe((int)HttpStatusCode.OK);
        second.Status.ShouldBe((int)HttpStatusCode.OK);
        third.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        third.RetryAfter.ShouldBe("60");
        third.Code.ShouldBe("test.limit-reached");
        third.Title.ShouldBe(new TestLimitReached().Message);
        invoked.ShouldBe(2);
    }

    [Fact]
    public async Task Another_address_is_still_served()
    {
        await GetAsync(Address);
        await GetAsync(Address);
        (await GetAsync(Address)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);

        var other = await GetAsync(OtherAddress);

        other.Status.ShouldBe((int)HttpStatusCode.OK);
        other.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task After_the_window_the_address_is_served_again()
    {
        await GetAsync(Address);
        await GetAsync(Address);
        (await GetAsync(Address)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        time.Advance(TimeSpan.FromMinutes(1));

        var admitted = await GetAsync(Address);

        admitted.Status.ShouldBe((int)HttpStatusCode.OK);
        invoked.ShouldBe(3);
    }

    [Fact]
    public async Task The_address_is_counted_from_the_forwarded_address_the_edge_set()
    {
        await GetAsync(OtherAddress, forwardedFor: "203.0.113.9");
        await GetAsync(OtherAddress, forwardedFor: "203.0.113.9");

        var refused = await GetAsync(OtherAddress, forwardedFor: "203.0.113.9");
        var connection = await GetAsync(OtherAddress);

        refused.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        connection.Status.ShouldBe((int)HttpStatusCode.OK, "the edge's own address is another caller's");
    }

    [Fact]
    public async Task Signing_in_as_different_users_does_not_escape_the_address_limit()
    {
        await GetAsync(Address, subject: Guid.CreateVersion7().ToString());
        await GetAsync(Address, subject: Guid.CreateVersion7().ToString());

        var refused = await GetAsync(Address, subject: Guid.CreateVersion7().ToString());

        refused.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_refusal_is_logged_with_the_limits_name_and_never_the_address()
    {
        await GetAsync(Address);
        await GetAsync(Address);

        (await GetAsync(Address)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);

        var records = app.Services.GetFakeLogCollector().GetSnapshot();
        var refusal = records.Where(record => record.Message.Contains(LimitName, StringComparison.Ordinal)).ShouldHaveSingleItem();
        foreach (var record in records)
        {
            record.Message.ShouldNotContain("203.0.113.9");
            record.Message.ShouldNotContain("address:");
            (record.StructuredState ?? []).ShouldNotContain(pair => pair.Value != null && pair.Value.Contains("203.0.113.9", StringComparison.Ordinal));
        }

        refusal.Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task An_endpoint_naming_an_unregistered_limit_fails_when_its_endpoints_are_built()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddUserRateLimit(UserRateLimit.Of(LimitName, new TestLimitReached(), UserRateWindow.Of(2, TimeSpan.FromMinutes(1))));
        await using var misnamed = builder.Build();
        misnamed.MapGet("/typo", () => TypedResults.Ok()).AllowAnonymous().RequireClientRateLimit("test.verfiy");

        var build = () => ((IEndpointRouteBuilder)misnamed).DataSources.SelectMany(source => source.Endpoints).ToList();

        Should.Throw<InvalidOperationException>(build).Message.ShouldContain("test.verfiy");
    }

    [Fact]
    public void The_endpoint_declares_a_429_problem_response()
    {
        Endpoint().Metadata.OfType<IProducesResponseTypeMetadata>().ShouldContain(produces => produces.StatusCode == StatusCodes.Status429TooManyRequests);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private RouteEndpoint Endpoint() =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == Verify);

    private async Task<Answer> GetAsync(IPAddress remote, string? forwardedFor = null, string? subject = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (subject is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], authenticationType: "Bearer"));
        }

        context.Connection.RemoteIpAddress = remote;
        if (forwardedFor is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = Verify;
        context.Response.Body = body;

        await Endpoint().RequestDelegate!(context);

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
