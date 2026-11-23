using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
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

    private const string Json = "application/json";

    /// <summary>
    /// A body the endpoint cannot turn into a reason, each with the content type it is sent with: no reason, a blank one,
    /// one a character longer than the maximum, one holding RLO or an unpaired surrogate (escaped in the JSON); an empty
    /// body, malformed JSON, JSON that is no request object (<c>null</c>, <c>[]</c>, a number as the reason); a reason
    /// sent with no content type, in UTF-16 or Latin-1 as its charset says, and bytes that are not UTF-8 (#181, S2).
    /// </summary>
    public static TheoryData<string?, byte[]> WrongReasons =>
    [
        (Json, Utf8("{}")),
        (Json, Utf8("""{"reason":"   "}""")),
        (Json, Utf8($$"""{"reason":"{{new string('a', 1001)}}"}""")),
        (Json, Utf8("""{"reason":"a\u202Eb"}""")),
        (Json, Utf8("""{"reason":"a\ud800b"}""")),
        (Json, []),
        (Json, Utf8("""{"reason":"Quelle fehlt.""")),
        (Json, Utf8("null")),
        (Json, Utf8("[]")),
        (Json, Utf8("""{"reason":5}""")),
        (null, Utf8(AReason)),
        ("application/json; charset=utf-16", Encoding.Unicode.GetBytes(AReason)),
        ("application/json; charset=latin1", Encoding.Latin1.GetBytes(AReason)),
        (Json, [.. Utf8("""{"reason":"a"""), 0xFF, 0xFE, .. Utf8("""b"}""")]),
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
    public async Task A_return_with_a_reason_the_caller_got_wrong_is_refused_before_it_is_dispatched(string? contentType, byte[] body)
    {
        var dispatcher = Dispatcher();
        var sent = $"{contentType ?? "no content type"}: {Convert.ToHexString(body)}";

        var (status, code) = await PostAsync("return", Editor(), dispatcher, contentType, body);

        status.ShouldBe((int)HttpStatusCode.BadRequest, $"POST …/return with {sent}");
        code.ShouldBe("editorial.invalid-return-reason", $"POST …/return with {sent}");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The caller's person is known before the body is read: one without a person is refused as such, whatever the body.</summary>
    [Theory]
    [InlineData(Json, "{}")]
    [InlineData(Json, "{\"reason\":")]
    [InlineData("text/plain", "{\"reason\":\"x\"}")]
    [InlineData(null, "")]
    public async Task A_caller_without_a_person_who_sends_a_wrong_reason_is_refused_as_without_a_person(string? contentType, string body)
    {
        var dispatcher = Dispatcher();
        var withoutPerson = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Erika Editor")], authenticationType: "Bearer"));

        var (status, code) = await PostAsync("return", withoutPerson, dispatcher, contentType, Utf8(body));

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"POST …/return with {contentType ?? "no content type"}: {body}");
        code.ShouldBe("editorial.identity-incomplete");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_return_dispatches_the_trimmed_reason()
    {
        var dispatcher = Dispatcher();

        var (status, _) = await PostAsync("return", Editor(), dispatcher, Json, Utf8("""{"reason":"  Quelle fehlt. "}"""));

        status.ShouldBe((int)HttpStatusCode.OK);
        await dispatcher.Received(1).SendAsync(
            Arg.Is<ICommand<Result<Unit, ApiError>>>(command => command is ReturnToDraft && ((ReturnToDraft)command).Reason.Value == "Quelle fehlt."),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A return records who took it under the name the token carries (#220): a caller whose token names no usable
    /// display name is refused with <c>editorial.reviewer-name-missing</c>, as an approval is (#205), and nothing is
    /// dispatched.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    public async Task A_caller_without_a_name_is_refused_with_reviewer_name_missing_before_the_return_is_dispatched(string? name)
    {
        var dispatcher = Dispatcher();

        var (status, code) = await PostAsync("return", EditorNamed(name), dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"POST …/return with the name claim {name ?? "missing"}");
        code.ShouldBe("editorial.reviewer-name-missing");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The name belongs to the caller and is checked before the body is read: no name wins over a wrong reason (#220).</summary>
    [Theory]
    [InlineData(Json, "{}")]
    [InlineData(Json, "{\"reason\":")]
    [InlineData("text/plain", "{\"reason\":\"x\"}")]
    [InlineData(null, "")]
    public async Task A_caller_without_a_name_who_sends_a_wrong_reason_is_refused_as_without_a_name(string? contentType, string body)
    {
        var dispatcher = Dispatcher();

        var (status, code) = await PostAsync("return", EditorNamed(null), dispatcher, contentType, Utf8(body));

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"POST …/return with {contentType ?? "no content type"}: {body}");
        code.ShouldBe("editorial.reviewer-name-missing");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_return_dispatches_the_callers_trimmed_name()
    {
        var dispatcher = Dispatcher();

        var (status, _) = await PostAsync("return", EditorNamed("  Erika Editor  "), dispatcher);

        status.ShouldBe((int)HttpStatusCode.OK);
        await dispatcher.Received(1).SendAsync(
            Arg.Is<ICommand<Result<Unit, ApiError>>>(command => command is ReturnToDraft && ((ReturnToDraft)command).EditorName == ReviewerName.From("Erika Editor")),
            Arg.Any<CancellationToken>());
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

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

    /// <summary>
    /// A signed-in editor whose token names their person and, unless <paramref name="name"/> is null, carries it as the
    /// <c>name</c> claim; <c>preferred_username</c> is there too, and is never read for the name.
    /// </summary>
    private static ClaimsPrincipal EditorNamed(string? name)
    {
        List<Claim> claims = [new Claim("sub", Guid.CreateVersion7().ToString()), new Claim("preferred_username", "editor")];
        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    /// <summary>
    /// Posts the step; a return carries <paramref name="body"/> (a reason by default) as <paramref name="contentType"/>,
    /// with no content type header when that is null, every other step no body.
    /// </summary>
    private static async Task<(int Status, string? Code)> PostAsync(
        string step, ClaimsPrincipal user, IDispatcher dispatcher, string? contentType = Json, byte[]? body = null)
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
            var bytes = body ?? Utf8(AReason);
            context.Request.ContentType = contentType;
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
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
