using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http.Tests;

/// <summary>
/// A failed guard answers <c>invalid-argument</c> with one fixed English title and detail (ADR 0027): the guard's own
/// message, which may be German or name the rejected value, is logged, never sent.
/// </summary>
public class GuardRefusalTests
{
    [Fact]
    public async Task A_failed_guard_answers_400_invalid_argument_without_the_guards_own_message()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddErrorResponses();
        await using var app = builder.Build();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Headers.Accept = "application/json";
        using var body = new MemoryStream();
        context.Response.Body = body;
        var guard = new GuardException("Ein Name enthält keine Steuer- oder Richtungszeichen. (Parameter 'name')");

        var handled = false;
        foreach (var handler in app.Services.GetServices<IExceptionHandler>())
        {
            handled = handled || await handler.TryHandleAsync(context, guard, TestContext.Current.CancellationToken);
        }

        handled.ShouldBeTrue("no exception handler took the failed guard");
        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(body.ToArray());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("invalid-argument");
        problem.RootElement.GetProperty("title").GetString().ShouldBe("The request is invalid.");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe("The request is invalid.");
        body.ToArray().ShouldNotContain((byte)0xC3, "the problem carries the guard's German message");
    }
}
