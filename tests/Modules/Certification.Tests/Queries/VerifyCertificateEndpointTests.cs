using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.VerifyCertificate;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Queries;

/// <summary>
/// <c>GET /certification/verify/{code}</c> (#51) answers anyone, without a token, under the per-address limit lowered
/// to 2 a minute through the module's own keys: 200 with what the certificate shows, 404 for a code that finds
/// nothing, 429 <c>certification.verification-limit-reached</c> over the limit, and none of them kept by any cache.
/// The request delegate is invoked directly, as after authorization.
/// </summary>
public sealed class VerifyCertificateEndpointTests : IAsyncDisposable
{
    private const string Route = "/verify/{code}";
    private const string Unknown = "k7Qp2xV9mL4rT8wZ1nB6cY3hAbCdEfGh";

    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.9");

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 6, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryCertificateStore store = new();
    private readonly Certificate certificate = IssuedCertificates.Yearly();
    private readonly WebApplication app;

    public VerifyCertificateEndpointTests()
    {
        store.Stored.Add(certificate);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [CertificationVerificationLimits.PerMinuteKey] = "2" })
            .Build();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddUserRateLimit(CertificationVerificationLimits.From(configuration));
        builder.Services.AddDispatcher();
        builder.Services.AddScoped<IQueryHandler<VerifyCertificate, Result<CertificateVerificationDto, ApiError>>, VerifyCertificateHandler>();
        builder.Services.AddSingleton<ICertificateStore>(store);
        app = builder.Build();
        new VerifyCertificateEndpoint().Map(app);
    }

    [Fact]
    public async Task A_stored_code_answers_200_with_what_the_certificate_shows()
    {
        var answer = await GetAsync(certificate.VerificationCode.Value);

        answer.Status.ShouldBe((int)HttpStatusCode.OK);
        answer.Body.ShouldNotBeNull();
        using var json = JsonDocument.Parse(answer.Body);
        json.RootElement.GetProperty("holderName").GetString().ShouldBe("Anna Berger");
        json.RootElement.GetProperty("courseTitle").GetString().ShouldBe("Hygiene 2027");
        json.RootElement.GetProperty("issuedOn").GetString().ShouldBe("2027-03-02");
        json.RootElement.GetProperty("validUntil").GetString().ShouldBe("2028-03-02");
        json.RootElement.GetProperty("validity").GetString().ShouldBe("Valid");
        answer.CacheControl.ShouldBe("no-store");
    }

    [Fact]
    public async Task An_unknown_code_answers_404_not_verifiable()
    {
        var answer = await GetAsync(Unknown);

        answer.Status.ShouldBe((int)HttpStatusCode.NotFound);
        Code(answer).ShouldBe("certification.certificate-not-verifiable");
        answer.Body.ShouldNotBeNull().ShouldNotContain(Unknown);
        answer.CacheControl.ShouldBe("no-store");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("   ")]
    public async Task A_malformed_code_answers_the_same_404_and_never_400(string code)
    {
        var answer = await GetAsync(code);

        answer.Status.ShouldBe((int)HttpStatusCode.NotFound);
        Code(answer).ShouldBe("certification.certificate-not-verifiable");
    }

    [Fact]
    public async Task The_third_request_from_one_address_in_a_minute_answers_429_uncached()
    {
        var first = await GetAsync(certificate.VerificationCode.Value);
        var second = await GetAsync(Unknown);

        var third = await GetAsync(certificate.VerificationCode.Value);

        first.Status.ShouldBe((int)HttpStatusCode.OK);
        second.Status.ShouldBe((int)HttpStatusCode.NotFound, "a code that finds nothing counts too");
        third.Status.ShouldBe((int)HttpStatusCode.TooManyRequests);
        Code(third).ShouldBe("certification.verification-limit-reached");
        third.RetryAfter.ShouldBe("60");
        third.CacheControl.ShouldBe("no-store");
        store.VerificationLookups.ShouldBe(2, "the refused request does not reach the store");
    }

    [Fact]
    public void The_endpoint_is_open_to_anonymous_callers()
    {
        Endpoint().Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    [Fact]
    public void The_endpoint_declares_200_404_and_429()
    {
        var statuses = Endpoint().Metadata.OfType<IProducesResponseTypeMetadata>().Select(produces => produces.StatusCode).ToList();

        statuses.ShouldContain(StatusCodes.Status200OK);
        statuses.ShouldContain(StatusCodes.Status404NotFound);
        statuses.ShouldContain(StatusCodes.Status429TooManyRequests);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private RouteEndpoint Endpoint() =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == Route);

    private async Task<Answer> GetAsync(string code)
    {
        var endpoint = Endpoint();
        await using var scope = app.Services.CreateAsyncScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Connection.RemoteIpAddress = Address;
        context.Request.Method = HttpMethods.Get;
        context.Request.RouteValues["code"] = code;
        context.Response.Body = body;

        await endpoint.RequestDelegate!(context);

        var retryAfter = context.Response.Headers.RetryAfter;
        return new Answer(
            context.Response.StatusCode,
            retryAfter.Count == 0 ? null : retryAfter.ToString(),
            context.Response.Headers.CacheControl.ToString(),
            body.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    private static string? Code(Answer answer)
    {
        if (answer.Body is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(answer.Body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record Answer(int Status, string? RetryAfter, string CacheControl, string? Body);
}
