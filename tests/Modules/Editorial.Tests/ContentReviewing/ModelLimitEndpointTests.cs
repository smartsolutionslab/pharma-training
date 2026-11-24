using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ReviewContent;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Tests.Fakes;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// Both endpoints that call the model carry the per-editor limit (#174), each its own: over it, an editor gets 429
/// <c>editorial.model-limit-reached</c> with <c>Retry-After</c>, and the model is not asked. The limits are lowered to 2
/// a minute through the module's own configuration keys. The request delegates are invoked directly, with
/// authorization already passed.
/// </summary>
public sealed class ModelLimitEndpointTests : IAsyncDisposable
{
    private const string ContentReviewRoute = "/course-versions/{id:guid}/content-review";
    private const string TestDraftRoute = "/course-versions/{id:guid}/test-draft";

    private static readonly string Editor = Guid.CreateVersion7().ToString();

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChatClient model = new(ReviewAnswers.Sound(1));
    private readonly InMemoryCourseVersionStore store = new();
    private readonly Guid version;
    private readonly WebApplication app;

    public ModelLimitEndpointTests()
    {
        var reviewed = ContentReviewVersions.WithTextChapters("Händehygiene");
        store.Stored.Add(reviewed);
        version = reviewed.Id.Value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EditorialModelLimits.ContentReviewPerMinuteKey] = "2",
                [EditorialModelLimits.TestDraftPerMinuteKey] = "2",
            })
            .Build();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<TimeProvider>(time);
        foreach (var limit in EditorialModelLimits.From(configuration))
        {
            builder.Services.AddUserRateLimit(limit);
        }

        builder.Services.AddDispatcher();
        builder.Services.AddScoped<IQueryHandler<ReviewContent, Result<EditorialContentReviewDto, ApiError>>, ReviewContentHandler>();
        builder.Services.AddSingleton<ICourseVersionStore>(store);
        builder.Services.AddSingleton<IChatClient>(model);
        app = builder.Build();
        new ContentReviewEndpoints().Map(app);
        new TestDraftEndpoints().Map(app);
    }

    [Fact]
    public async Task A_third_content_review_in_a_minute_answers_429_and_the_model_is_asked_twice()
    {
        var first = await PostAsync(ContentReviewRoute, version, Editor);
        var second = await PostAsync(ContentReviewRoute, version, Editor);

        var third = await PostAsync(ContentReviewRoute, version, Editor);

        first.Status.ShouldBe((int)HttpStatusCode.OK);
        second.Status.ShouldBe((int)HttpStatusCode.OK);
        third.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        third.Code.ShouldBe("editorial.model-limit-reached");
        third.RetryAfter.ShouldBe("60");
        model.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_third_test_draft_in_a_minute_answers_429()
    {
        var first = await PostAsync(TestDraftRoute, Guid.Empty, Editor);
        var second = await PostAsync(TestDraftRoute, Guid.Empty, Editor);

        var third = await PostAsync(TestDraftRoute, Guid.Empty, Editor);

        first.Status.ShouldBe((int)HttpStatusCode.NotFound);
        second.Status.ShouldBe((int)HttpStatusCode.NotFound);
        third.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        third.Code.ShouldBe("editorial.model-limit-reached");
        third.RetryAfter.ShouldBe("60");
    }

    [Fact]
    public async Task The_content_review_limit_does_not_use_up_the_test_draft_limit()
    {
        await PostAsync(ContentReviewRoute, version, Editor);
        await PostAsync(ContentReviewRoute, version, Editor);
        (await PostAsync(ContentReviewRoute, version, Editor)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);

        var draft = await PostAsync(TestDraftRoute, Guid.Empty, Editor);

        draft.Status.ShouldBe((int)HttpStatusCode.NotFound, "the test draft has a budget of its own");
    }

    [Fact]
    public async Task Another_editor_is_still_served()
    {
        await PostAsync(ContentReviewRoute, version, Editor);
        await PostAsync(ContentReviewRoute, version, Editor);
        (await PostAsync(ContentReviewRoute, version, Editor)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);

        var other = await PostAsync(ContentReviewRoute, version, Guid.CreateVersion7().ToString());

        other.Status.ShouldBe((int)HttpStatusCode.OK);
        model.Requests.Count.ShouldBe(3);
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        model.Dispose();
    }

    private async Task<Answer> PostAsync(string route, Guid id, string subject)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);
        await using var scope = app.Services.CreateAsyncScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], authenticationType: "Bearer")),
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.RouteValues["id"] = id.ToString();
        context.Response.Body = body;

        await endpoint.RequestDelegate!(context);

        var retryAfter = context.Response.Headers.RetryAfter;
        return new Answer(context.Response.StatusCode, retryAfter.Count == 0 ? null : retryAfter.ToString(), CodeOf(body));
    }

    private static string? CodeOf(MemoryStream body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body.ToArray());
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record Answer(int Status, string? RetryAfter, string? Code);
}
