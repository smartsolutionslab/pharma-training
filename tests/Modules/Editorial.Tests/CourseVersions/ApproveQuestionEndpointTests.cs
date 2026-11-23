using System.Net;
using System.Security.Claims;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Approving a question reads the caller's person and display name from the token before it dispatches (#205): a caller
/// without a person is refused with <c>editorial.identity-incomplete</c> first, one without a name that fits with
/// <c>editorial.reviewer-name-missing</c>, and neither reaches the dispatcher. The request delegate is invoked directly,
/// with authorization already passed.
/// </summary>
public class ApproveQuestionEndpointTests
{
    private static readonly Guid Subject = Guid.CreateVersion7();

    [Fact]
    public async Task A_caller_without_a_person_is_refused_with_identity_incomplete_before_the_approval_is_dispatched()
    {
        var dispatcher = Dispatcher();

        var (status, code) = await ApproveAsync(User(("name", "Emma Editor")), dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden);
        code.ShouldBe("editorial.identity-incomplete");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    public async Task A_caller_without_a_name_is_refused_with_reviewer_name_missing_before_the_approval_is_dispatched(string? name)
    {
        var dispatcher = Dispatcher();
        List<(string Type, string Value)> claims = [("sub", Subject.ToString()), ("preferred_username", "editor")];
        if (name is not null)
        {
            claims.Add(("name", name));
        }

        var (status, code) = await ApproveAsync(User([.. claims]), dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden, $"POST …/approve with the name claim {name ?? "missing"}");
        code.ShouldBe("editorial.reviewer-name-missing");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_caller_without_a_person_or_a_name_is_refused_as_without_a_person()
    {
        var dispatcher = Dispatcher();

        var (status, code) = await ApproveAsync(User(("preferred_username", "editor")), dispatcher);

        status.ShouldBe((int)HttpStatusCode.Forbidden);
        code.ShouldBe("editorial.identity-incomplete");
        await dispatcher.DidNotReceive().SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_approval_dispatches_the_callers_person_and_trimmed_name()
    {
        var dispatcher = Dispatcher();

        var (status, _) = await ApproveAsync(User(("sub", Subject.ToString()), ("name", "  Emma Editor ")), dispatcher);

        status.ShouldBe((int)HttpStatusCode.OK);
        await dispatcher.Received(1).SendAsync(
            Arg.Is<ICommand<Result<Unit, ApiError>>>(command =>
                command is ApproveQuestion
                && ((ApproveQuestion)command).Approver == PersonId.From(Subject)
                && ((ApproveQuestion)command).ApproverName == ReviewerName.From("Emma Editor")),
            Arg.Any<CancellationToken>());
    }

    private static IDispatcher Dispatcher()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Unit, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit, ApiError>.Success(Unit.Value));
        return dispatcher;
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));

    private static async Task<(int Status, string? Code)> ApproveAsync(ClaimsPrincipal user, IDispatcher dispatcher)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(dispatcher);
        await using var app = builder.Build();
        new QuestionEndpoints().Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/course-versions/{id:guid}/questions/{questionId:guid}/approve");

        using var response = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = HttpMethods.Post;
        context.Request.RouteValues["id"] = Guid.CreateVersion7().ToString();
        context.Request.RouteValues["questionId"] = Guid.CreateVersion7().ToString();
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
