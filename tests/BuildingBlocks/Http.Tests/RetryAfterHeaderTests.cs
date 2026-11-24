using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http.Tests;

/// <summary>
/// A refusal to retry later (<see cref="ApiError.RetryLater"/>) answers 429 with a <c>Retry-After</c> header in whole
/// seconds, rounded up and at least one, and the instant itself as <c>retryAt</c> beside <c>code</c> in the problem.
/// The endpoints are mapped the way every module's are, so the header comes from the module's route group, and their
/// request delegates are invoked directly on the host's <see cref="TimeProvider"/>.
/// </summary>
public class RetryAfterHeaderTests
{
    private static readonly DateTimeOffset Now = new(2027, 4, 1, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_refusal_to_retry_later_answers_429_with_retry_after_in_whole_seconds_and_retry_at_in_the_problem()
    {
        var answer = await InvokeAsync(HttpMethods.Get, "/refusals/later");

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        answer.RetryAfter.ShouldBe("91", "Retry-After of a refusal 90.2 seconds before it may be retried");
        using var problem = JsonDocument.Parse(answer.Body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("refusals.too-soon");
        problem.RootElement.TryGetProperty("retryAt", out var retryAt).ShouldBeTrue("the problem carries no retryAt");
        DateTimeOffset.Parse(retryAt.GetString()!, CultureInfo.InvariantCulture).ShouldBe(Now + RetryLaterEndpoints.Wait);
    }

    [Fact]
    public async Task A_retry_at_already_past_answers_retry_after_of_one_second()
    {
        var answer = await InvokeAsync(HttpMethods.Get, "/refusals/past");

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        answer.RetryAfter.ShouldBe("1", "Retry-After of a refusal whose instant has passed");
    }

    [Fact]
    public async Task A_created_result_refused_to_retry_later_carries_retry_after_too()
    {
        var answer = await InvokeAsync(HttpMethods.Post, "/refusals/later-create");

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        answer.RetryAfter.ShouldBe("91", "Retry-After of a create refused 90.2 seconds before it may be retried");
        using var problem = JsonDocument.Parse(answer.Body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("refusals.too-soon");
        problem.RootElement.TryGetProperty("retryAt", out _).ShouldBeTrue("the problem carries no retryAt");
    }

    [Fact]
    public async Task Any_other_problem_has_no_retry_after()
    {
        var answer = await InvokeAsync(HttpMethods.Get, "/refusals/conflict");

        answer.Status.ShouldBe((int)HttpStatusCode.Conflict);
        answer.RetryAfter.ShouldBeEmpty();
        using var problem = JsonDocument.Parse(answer.Body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("refusals.too-soon");
        problem.RootElement.TryGetProperty("retryAt", out _).ShouldBeFalse("a conflict carries a retryAt");
    }

    // #294: a refusal that carries its instant counts from it, so a host whose clock is off answers the same wait.
    [Theory]
    [InlineData("/refusals/later-ahead")]
    [InlineData("/refusals/later-behind")]
    public async Task A_refusal_that_carries_its_instant_counts_retry_after_from_it_not_the_host_clock(string route)
    {
        var refusedAt = route == "/refusals/later-ahead" ? Now + RetryLaterEndpoints.Ahead : Now - RetryLaterEndpoints.Behind;

        var answer = await InvokeAsync(HttpMethods.Get, route);

        answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        answer.RetryAfter.ShouldBe("91", $"Retry-After of a refusal 90.2 seconds before it may be retried, counted from the instant it was refused at ({route})");
        using var problem = JsonDocument.Parse(answer.Body);
        problem.RootElement.TryGetProperty("refusedAt", out var refused).ShouldBeTrue("the problem carries no refusedAt");
        DateTimeOffset.Parse(refused.GetString()!, CultureInfo.InvariantCulture).ShouldBe(refusedAt);
        DateTimeOffset.Parse(problem.RootElement.GetProperty("retryAt").GetString()!, CultureInfo.InvariantCulture).ShouldBe(refusedAt + RetryLaterEndpoints.Wait);
    }

    [Fact]
    public async Task A_refusal_without_its_instant_carries_no_refused_at()
    {
        var answer = await InvokeAsync(HttpMethods.Get, "/refusals/later");

        using var problem = JsonDocument.Parse(answer.Body);
        problem.RootElement.TryGetProperty("refusedAt", out _).ShouldBeFalse("a refusal without its instant carries a refusedAt");
    }

    private static async Task<(int Status, string RetryAfter, byte[] Body)> InvokeAsync(string method, string route)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        await using var app = builder.Build();
        new RetryLaterModule().MapEndpoints(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == route
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
        endpoint.ShouldNotBeNull($"the module maps no {method} {route}");

        using var response = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = method;
        context.Response.Body = response;

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, context.Response.Headers.RetryAfter.ToString(), response.ToArray());
    }
}
