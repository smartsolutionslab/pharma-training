using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RegisterPharmacy;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>
/// <c>POST /pharmacies</c> registers a pharmacy only for a caller whose token says Keycloak verified the address: a
/// missing <c>email_verified</c> claim, or one that does not read as <see langword="true"/>, is refused with 403 and
/// nothing is stored. Keycloak never issues a token with an address but without the claim from this realm, so the
/// endpoint's request delegate is invoked directly with the claims a JWT handler leaves, and the dispatcher forwards to
/// the real handler over the in-memory store.
/// </summary>
public class RegisterPharmacyEndpointTests
{
    private const string Route = "/pharmacies";
    private const string Body = """{"name":"Stadt-Apotheke","jobFunction":"Pharmacist"}""";

    private static readonly DateTimeOffset Start = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_caller_whose_token_has_no_email_verified_claim_is_refused_with_403_and_no_pharmacy_is_stored()
    {
        var store = new InMemoryPharmacyStore();

        var answer = await PostAsync(Caller(emailVerified: null), store);

        answer.Status.ShouldBe((int)HttpStatusCode.Forbidden, "POST /pharmacies by a caller whose token has no email_verified claim");
        CodeOf(answer.Body).ShouldBe("organisation.email-not-verified");
        store.Stored.ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("yes")]
    public async Task A_caller_whose_email_verified_claim_is_not_true_is_refused_with_403_and_no_pharmacy_is_stored(string claim)
    {
        var store = new InMemoryPharmacyStore();

        var answer = await PostAsync(Caller(emailVerified: claim), store);

        answer.Status.ShouldBe((int)HttpStatusCode.Forbidden, $"POST /pharmacies by a caller whose email_verified claim is \"{claim}\"");
        CodeOf(answer.Body).ShouldBe("organisation.email-not-verified");
        store.Stored.ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_caller_whose_email_verified_claim_is_true_registers_with_201_and_the_pharmacy_is_stored()
    {
        var store = new InMemoryPharmacyStore();

        var answer = await PostAsync(Caller(emailVerified: "true"), store);

        answer.Status.ShouldBe((int)HttpStatusCode.Created);
        var id = JsonSerializer.Deserialize<Guid>(answer.Body);
        answer.Location.ShouldBe($"/organisation/pharmacies/{id}");
        store.Stored.ShouldHaveSingleItem().Id.Value.ShouldBe(id);
    }

    private static ClaimsPrincipal Caller(string? emailVerified)
    {
        List<Claim> claims =
        [
            new("sub", Guid.CreateVersion7().ToString()),
            new("name", "Olaf Owner"),
            new("email", "owner@example.com"),
        ];
        if (emailVerified is not null)
        {
            claims.Add(new Claim("email_verified", emailVerified));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    private static IDispatcher Dispatching(InMemoryPharmacyStore store)
    {
        var handler = new RegisterPharmacyHandler(store, store, new FakeTimeProvider(Start));
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.SendAsync(Arg.Any<ICommand<Result<Guid, ApiError>>>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.HandleAsync((RegisterPharmacy)call.ArgAt<ICommand<Result<Guid, ApiError>>>(0), call.ArgAt<CancellationToken>(1)));
        return dispatcher;
    }

    private static async Task<(int Status, string? Location, byte[] Body)> PostAsync(ClaimsPrincipal user, InMemoryPharmacyStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(Dispatching(store));
        await using var app = builder.Build();
        new PharmacyEndpoints().Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == Route
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);
        endpoint.ShouldNotBeNull($"PharmacyEndpoints maps no POST {Route}");

        using var response = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        var request = Encoding.UTF8.GetBytes(Body);
        context.Request.Body = new MemoryStream(request);
        context.Request.ContentLength = request.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new WithBody());
        context.Response.Body = response;

        await endpoint.RequestDelegate!(context);

        return (context.Response.StatusCode, context.Response.Headers.Location.ToString(), response.ToArray());
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

    /// <summary>Tells the request delegate the request has a body, as a server does for a POST that sends one.</summary>
    private sealed class WithBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
