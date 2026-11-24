using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http.Tests;

/// <summary>Endpoints that refuse the way a module's do: through a Result and its <c>To*HttpResult</c>.</summary>
public sealed class RetryLaterEndpoints : IEndpoint
{
    /// <summary>How far after the host's now the refusals of <c>/later</c> and <c>/later-create</c> may be retried.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(90.2);

    /// <summary>How far the clock that refused <c>/later-ahead</c> is ahead of the host's (#294).</summary>
    public static readonly TimeSpan Ahead = TimeSpan.FromDays(3);

    /// <summary>How far the clock that refused <c>/later-behind</c> is behind the host's (#294).</summary>
    public static readonly TimeSpan Behind = TimeSpan.FromHours(1);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/later", (TimeProvider time) =>
            Result<string, ApiError>.Failure(ApiError.RetryLater(new TooSoon(), time.GetUtcNow() + Wait)).ToHttpResult());

        // Refused on another clock than the host's, as the database's: the refusal carries the instant it was refused at.
        endpoints.MapGet("/later-ahead", (TimeProvider time) =>
        {
            var refusedAt = time.GetUtcNow() + Ahead;
            return Result<string, ApiError>.Failure(ApiError.RetryLater(new TooSoon(), refusedAt + Wait, refusedAt)).ToHttpResult();
        });

        endpoints.MapGet("/later-behind", (TimeProvider time) =>
        {
            var refusedAt = time.GetUtcNow() - Behind;
            return Result<string, ApiError>.Failure(ApiError.RetryLater(new TooSoon(), refusedAt + Wait, refusedAt)).ToHttpResult();
        });

        endpoints.MapGet("/past", (TimeProvider time) =>
            Result<string, ApiError>.Failure(ApiError.RetryLater(new TooSoon(), time.GetUtcNow() - TimeSpan.FromSeconds(5))).ToHttpResult());

        endpoints.MapPost("/later-create", (TimeProvider time) =>
            Result<string, ApiError>.Failure(ApiError.RetryLater(new TooSoon(), time.GetUtcNow() + Wait)).ToCreatedHttpResult(value => $"/refusals/{value}"));

        endpoints.MapGet("/conflict", () =>
            Result<string, ApiError>.Failure(ApiError.From(new TooSoon(), HttpStatusCode.Conflict)).ToHttpResult());
    }

    /// <summary>A refusal for now, not for good.</summary>
    public sealed record TooSoon() : DomainError("refusals.too-soon", "Asked a moment ago; ask again later.");
}
