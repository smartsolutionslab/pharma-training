using System.Net;
using System.Security.Claims;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// <c>POST /course-versions/{id}/next</c> resolves the calling editor's person and refuses the empty Guid before
/// anything is dispatched, and answers a drafted version with 201, its location and its id. The endpoint's request
/// delegate is invoked directly, with authorization already passed.
/// </summary>
public class NextVersionEndpointTests
{
    private const string Route = "/course-versions/{id:guid}/next";

    private static readonly ClaimsPrincipal Editor = new(new ClaimsIdentity(
        [new Claim("name", "Erika Editor"), new Claim("sub", Guid.CreateVersion7().ToString())],
        authenticationType: "Bearer"));

    [Fact]
    public async Task A_caller_without_a_person_is_refused_before_the_next_version_is_dispatched()
    {
        var dispatcher = Dispatching(Guid.CreateVersion7());
        var withoutPerson = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Erika Editor")], authenticationType: "Bearer"));

        var answer = await PostAsync(Guid.CreateVersion7(), withoutPerson, dispatcher);

        answer.Status.ShouldBe((int)HttpStatusCode.Forbidden, "POST …/next by a caller whose token names no person");
        CodeOf(answer.Body).ShouldBe("editorial.identity-incomplete");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_empty_guid_is_not_found_before_anything_is_dispatched()
    {
        var dispatcher = Dispatching(Guid.CreateVersion7());

        var answer = await PostAsync(Guid.Empty, Editor, dispatcher);

        answer.Status.ShouldBe((int)HttpStatusCode.NotFound);
        CodeOf(answer.Body).ShouldBe("editorial.course-version-not-found");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_next_version_answers_201_with_its_location_and_id()
    {
        var newId = Guid.CreateVersion7();
        var dispatcher = Dispatching(newId);

        var answer = await PostAsync(Guid.CreateVersion7(), Editor, dispatcher);

        answer.Status.ShouldBe((int)HttpStatusCode.Created);
        answer.Location.ShouldBe($"/editorial/course-versions/{newId}");
        JsonSerializer.Deserialize<Guid>(answer.Body).ShouldBe(newId);
    }

    private static IDispatcher Dispatching(Guid newId)
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid, ApiError>.Success(newId));
        return dispatcher;
    }

    private static async Task<(int Status, string? Location, byte[] Body)> PostAsync(Guid id, ClaimsPrincipal user, IDispatcher dispatcher)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(dispatcher);
        await using var app = builder.Build();
        new CourseEndpoints().Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == Route);
        endpoint.ShouldNotBeNull($"CourseEndpoints maps no POST {Route}");

        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = HttpMethods.Post;
        context.Request.RouteValues["id"] = id.ToString();
        context.Response.Body = body;

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, context.Response.Headers.Location.ToString(), body.ToArray());
    }

    private static string? CodeOf(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
