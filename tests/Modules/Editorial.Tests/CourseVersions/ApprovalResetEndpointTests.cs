using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.EditQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SetTestBlueprint;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The three PUT routes that can take an approval away read the caller's person from the token and pass it on as the
/// command's editor (#246): a caller without a person is refused with <c>editorial.identity-incomplete</c>, after the
/// empty ids and before the body is checked, and nothing is dispatched. The request delegate is invoked directly, with
/// authorization already passed.
/// </summary>
public class ApprovalResetEndpointTests
{
    private const string Blueprint = "set the test blueprint";
    private const string Question = "edit a question";
    private const string TextChapter = "replace a chapter with text";
    private const string FileChapter = "replace a chapter with a file";

    private static readonly Guid Subject = Guid.CreateVersion7();

    [Theory]
    [InlineData(Blueprint)]
    [InlineData(Question)]
    [InlineData(TextChapter)]
    [InlineData(FileChapter)]
    public async Task A_caller_without_a_person_is_refused_with_identity_incomplete_and_nothing_is_dispatched(string route)
    {
        var dispatcher = Dispatcher();

        var (status, code) = await PutAsync(route, ValidBody(route), User(("name", "Emma Editor"), ("preferred_username", "editor")), dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"{route} without a person");
        code.ShouldBe("editorial.identity-incomplete");
        dispatcher.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Identity comes before the body, as on the approve and return routes.</summary>
    [Theory]
    [InlineData(Blueprint)]
    [InlineData(Question)]
    [InlineData(TextChapter)]
    public async Task A_caller_without_a_person_is_refused_as_without_a_person_before_a_body_that_is_wrong(string route)
    {
        var dispatcher = Dispatcher();

        var (status, code) = await PutAsync(route, InvalidBody(route), User(("name", "Emma Editor")), dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"{route} without a person and with a body that is wrong");
        code.ShouldBe("editorial.identity-incomplete");
        dispatcher.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Blueprint)]
    [InlineData(Question)]
    [InlineData(TextChapter)]
    [InlineData(FileChapter)]
    public async Task The_callers_person_is_dispatched_as_the_commands_editor(string route)
    {
        var dispatcher = Dispatcher();

        var (status, _) = await PutAsync(route, ValidBody(route), User(("sub", Subject.ToString()), ("name", "Emma Editor")), dispatcher);

        status.ShouldBe((int)HttpStatusCode.OK);
        var command = dispatcher.ReceivedCalls().ShouldHaveSingleItem().GetArguments()[0];
        var editor = command switch
        {
            SetTestBlueprint set => set.Editor,
            EditQuestion edit => edit.Editor,
            ReplaceTextChapter text => text.Editor,
            ReplaceFileChapter file => file.Editor,
            _ => throw new InvalidOperationException($"{route} dispatched {command}."),
        };
        editor.ShouldBe(PersonId.From(Subject));
    }

    private static string ValidBody(string route) => route switch
    {
        Blueprint => """{ "questions": 10, "passingScorePercent": 80, "validityMonths": 12 }""",
        Question => $$"""{ "chapterId": "{{Guid.CreateVersion7()}}", "text": "Wann desinfizieren Sie die Hände?", "type": "SingleChoice", "options": [ { "text": "Vor der Rezeptur", "correct": true }, { "text": "Einmal am Tag", "correct": false } ] }""",
        TextChapter => """{ "title": "Händehygiene", "text": "Neu: vor und nach …" }""",
        FileChapter => $$"""{ "title": "Hygieneplan", "kind": "Pdf", "file": "course-versions/{{Guid.CreateVersion7()}}/plan.pdf" }""",
        _ => throw new InvalidOperationException($"No route is called '{route}'."),
    };

    /// <summary>A body that binds but that the route refuses: a passing score out of range, a blank text, a blank title.</summary>
    private static string InvalidBody(string route) => route switch
    {
        Blueprint => """{ "questions": 10, "passingScorePercent": 101, "validityMonths": 12 }""",
        Question => $$"""{ "chapterId": "{{Guid.CreateVersion7()}}", "text": " ", "type": "SingleChoice", "options": [ { "text": "Vor der Rezeptur", "correct": true }, { "text": "Einmal am Tag", "correct": false } ] }""",
        TextChapter => """{ "title": " ", "text": "Neu: vor und nach …" }""",
        _ => throw new InvalidOperationException($"No route is called '{route}'."),
    };

    private static IEndpoint EndpointsOf(string route) => route switch
    {
        Blueprint => new BlueprintEndpoints(),
        Question => new QuestionEndpoints(),
        _ => new ChapterEndpoints(),
    };

    private static string PatternOf(string route) => route switch
    {
        Blueprint => "/course-versions/{id:guid}/blueprint",
        Question => "/course-versions/{id:guid}/questions/{questionId:guid}",
        _ => "/course-versions/{id:guid}/chapters/{chapterId:guid}",
    };

    private static IDispatcher Dispatcher()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit, ApiError>.Success(Unit.Value));
        return dispatcher;
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));

    private static async Task<(int Status, string? Code)> PutAsync(string route, string body, ClaimsPrincipal user, IDispatcher dispatcher)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(dispatcher);
        await using var app = builder.Build();
        EndpointsOf(route).Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == PatternOf(route)
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Put) == true);

        using var response = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = HttpMethods.Put;
        context.Request.RouteValues["id"] = Guid.CreateVersion7().ToString();
        context.Request.RouteValues["questionId"] = Guid.CreateVersion7().ToString();
        context.Request.RouteValues["chapterId"] = Guid.CreateVersion7().ToString();
        context.Request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new WithBody());
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

    /// <summary>Tells the request delegate the request has a body, as a server does for a PUT that sends one.</summary>
    private sealed class WithBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
