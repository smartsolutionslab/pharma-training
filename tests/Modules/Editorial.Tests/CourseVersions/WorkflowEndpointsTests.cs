using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Every workflow step on a version resolves the calling editor's person first, review 6012816888 (3): a caller whose
/// token names no person is refused with <c>editorial.identity-incomplete</c> before anything is dispatched, on return
/// as on submit and publish. A return carries its reason in the body, which the endpoint turns into a
/// <c>ReturnReason</c> before it dispatches, refusing one the caller got wrong with <c>editorial.invalid-return-reason</c>
/// (#181). Each endpoint's request delegate is invoked directly, with authorization already passed.
/// </summary>
public class WorkflowEndpointsTests
{
    private const string AReason = """{"reason":"Kapitel 2 braucht eine Quelle."}""";

    /// <summary>No reason, a blank one, one a character longer than the maximum, and one holding RLO (escaped in the JSON).</summary>
    public static TheoryData<string> WrongReasons =>
    [
        "{}",
        """{"reason":"   "}""",
        $$"""{"reason":"{{new string('a', 1001)}}"}""",
        """{"reason":"a\u202Eb"}""",
    ];

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

    [Theory]
    [MemberData(nameof(WrongReasons))]
    public async Task A_return_with_a_reason_the_caller_got_wrong_is_refused_before_it_is_dispatched(string body)
    {
        var dispatcher = Dispatcher();

        var (status, code) = await PostAsync("return", Editor(), dispatcher, body);

        status.ShouldBe((int)HttpStatusCode.BadRequest, $"POST …/return with {body}");
        code.ShouldBe("editorial.invalid-return-reason");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_return_dispatches_the_trimmed_reason()
    {
        var dispatcher = Dispatcher();

        var (status, _) = await PostAsync("return", Editor(), dispatcher, """{"reason":"  Quelle fehlt. "}""");

        status.ShouldBe((int)HttpStatusCode.OK);
        await dispatcher.Received(1).SendAsync(
            Arg.Is<ICommand<Result<Unit, ApiError>>>(command => command is ReturnToDraft && ((ReturnToDraft)command).Reason.Value == "Quelle fehlt."),
            Arg.Any<CancellationToken>());
    }

    private static IDispatcher Dispatcher()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit, ApiError>.Success(Unit.Value));
        return dispatcher;
    }

    /// <summary>A signed-in editor whose token names their person.</summary>
    private static ClaimsPrincipal Editor() =>
        new(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString()), new Claim("name", "Erika Editor")], authenticationType: "Bearer"));

    /// <summary>Posts the step; a return carries <paramref name="body"/>, a reason by default, every other step no body.</summary>
    private static async Task<(int Status, string? Code)> PostAsync(string step, ClaimsPrincipal user, IDispatcher dispatcher, string? body = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(dispatcher);
        await using var app = builder.Build();
        new WorkflowEndpoints().Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == $"/course-versions/{{id:guid}}/{step}");

        using var response = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = HttpMethods.Post;
        context.Request.RouteValues["id"] = Guid.CreateVersion7().ToString();
        if (step == "return")
        {
            var json = Encoding.UTF8.GetBytes(body ?? AReason);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = json.Length;
            context.Request.Body = new MemoryStream(json);
        }

        context.Response.Body = response;

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, CodeOf(response));
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
