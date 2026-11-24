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

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/later", (TimeProvider time) =>
            Result<string, ApiError>.Failure(ApiError.RetryLater(new TooSoon(), time.GetUtcNow() + Wait)).ToHttpResult());

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
