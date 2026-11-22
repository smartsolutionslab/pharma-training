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
/// Every workflow step on a version resolves the calling editor's person first, review 6012816888 (3): a caller whose
/// token names no person is refused with <c>editorial.identity-incomplete</c> before anything is dispatched, on return
/// as on submit and publish. Each endpoint's request delegate is invoked directly, with authorization already passed.
/// </summary>
public class WorkflowEndpointsTests
{
    [Theory]
    [InlineData("submit")]
    [InlineData("publish")]
    [InlineData("return")]
    public async Task A_caller_without_a_person_is_refused_before_the_step_is_dispatched(string step)
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit, ApiError>.Success(Unit.Value));
        var withoutPerson = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Erika Editor")], authenticationType: "Bearer"));

        var (status, code) = await PostAsync(step, withoutPerson, dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"POST …/{step} by a caller whose token names no person");
        code.ShouldBe("editorial.identity-incomplete");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    private static async Task<(int Status, string? Code)> PostAsync(string step, ClaimsPrincipal user, IDispatcher dispatcher)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(dispatcher);
        await using var app = builder.Build();
        new WorkflowEndpoints().Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == $"/course-versions/{{id:guid}}/{step}");

        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = HttpMethods.Post;
        context.Request.RouteValues["id"] = Guid.CreateVersion7().ToString();
        context.Response.Body = body;

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, CodeOf(body));
    }

    private static string? CodeOf(MemoryStream body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body.ToArray());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
