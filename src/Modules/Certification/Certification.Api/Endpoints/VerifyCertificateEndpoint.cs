using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.VerifyCertificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>
/// Public verification of a certificate by the code in its QR link (#51, Z2). Open to anyone: a verifier scans the code
/// without an account. Every code that finds nothing, malformed or not, answers the same 404; each client address may
/// verify a limited number of times a minute and a day (docs/adr/0029). No answer may be cached, as the validity changes
/// with the day and the body is personal data.
/// </summary>
public sealed class VerifyCertificateEndpoint : IEndpoint
{
    private const string NoStore = "no-store";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/verify/{code}", async (string code, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new VerifyCertificate(code), cancellationToken)).ToHttpResult())
            .AllowAnonymous()
            // The code is a credential: telemetry records the route's template, never the path (#311, docs/adr/0033).
            .WithMetadata(new RedactedRoute())
            .AddEndpointFilter(async (context, next) =>
            {
                // Before the limit's filter, so its 429 is not kept by any cache either.
                context.HttpContext.Response.Headers[HeaderNames.CacheControl] = NoStore;
                return await next(context);
            })
            .RequireClientRateLimit(CertificationVerificationLimits.Verify)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
