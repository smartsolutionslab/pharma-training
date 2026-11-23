using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// Accepting an invitation is limited per user (docs/adr/0004, docs/adr/0020): without configuration the 11th accept
/// of a user within a minute answers 429 <c>organisation.accept-limit-reached</c> with <c>Retry-After</c>, and the use
/// case does not run; another user is still served. The module's own services and endpoints are used as the host adds
/// them, with the dispatcher replaced by a substitute that accepts every call; the request delegate is invoked
/// directly, with authorization already passed.
/// </summary>
public sealed class AcceptLimitEndpointTests : IAsyncDisposable
{
    private const string Route = "/organisation/invitations/accept";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 4, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly IDispatcher dispatcher = Substitute.For<IDispatcher>();
    private readonly WebApplication app;

    public AcceptLimitEndpointTests()
    {
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid, ApiError>.Success(Guid.CreateVersion7()));

        var builder = WebApplication.CreateSlimBuilder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
            .Build();
        builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(_ => new SmartSolutionsLab.PharmaTraining.Tests.Fakes.FakeChatClient("unused"));
        new OrganisationModule().AddServices(builder.Services, configuration);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(dispatcher);
        app = builder.Build();
        new OrganisationModule().MapEndpoints(app);
    }

    [Fact]
    public async Task The_11th_accept_of_a_user_within_a_minute_answers_429_with_retry_after_and_the_modules_code_and_does_not_run()
    {
        var anna = Guid.CreateVersion7();
        for (var accept = 1; accept <= 10; accept++)
        {
            var admitted = await AcceptAsync(anna);
            admitted.Status.ShouldBe((int)HttpStatusCode.OK, $"accept {accept} of 10 is within the limit");
        }

        var refused = await AcceptAsync(anna);

        refused.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, "the 11th accept within a minute");
        refused.Code.ShouldBe("organisation.accept-limit-reached");
        refused.RetryAfter.ShouldNotBeNull("Retry-After in seconds");
        int.Parse(refused.RetryAfter, System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(1, 60);
        await dispatcher.Received(10).SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Another_user_is_still_served_when_one_has_reached_the_limit()
    {
        var anna = Guid.CreateVersion7();
        for (var accept = 1; accept <= 10; accept++)
        {
            await AcceptAsync(anna);
        }

        (await AcceptAsync(anna)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);

        var ben = await AcceptAsync(Guid.CreateVersion7());

        ben.Status.ShouldBe((int)HttpStatusCode.OK, "another user has a limit of their own");
    }

    [Fact]
    public async Task Once_the_minute_has_passed_the_user_is_served_again()
    {
        var anna = Guid.CreateVersion7();
        for (var accept = 1; accept <= 10; accept++)
        {
            await AcceptAsync(anna);
        }

        (await AcceptAsync(anna)).Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        time.Advance(TimeSpan.FromMinutes(1));

        var again = await AcceptAsync(anna);

        again.Status.ShouldBe((int)HttpStatusCode.OK, "a minute later the minute's window has room again");
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private async Task<Answer> AcceptAsync(Guid subject)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == Route);
        await using var scope = app.Services.CreateAsyncScope();
        using var response = new MemoryStream();
        var request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { token = RandomNumberGenerator.GetHexString(64) }));
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", subject.ToString()), new Claim("name", "Anna Accept"), new Claim("email", "anna@example.org"), new Claim("email_verified", "true")],
                authenticationType: "Bearer")),
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(request);
        context.Request.ContentLength = request.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new WithBody());
        context.Response.Body = response;

        await endpoint.RequestDelegate!(context);

        var retryAfter = context.Response.Headers.RetryAfter;
        return new Answer(context.Response.StatusCode, retryAfter.Count == 0 ? null : retryAfter.ToString(), CodeOf(response));
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

    /// <summary>Tells the request delegate the request has a body, as a server does for a POST that sends one.</summary>
    private sealed class WithBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
