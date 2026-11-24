using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetCertificateDocument;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>
/// A certificate's PDF for its holder (#50): a five-minute read URL on Certification's own blob, served inline. Open to
/// every signed-in person; the holder is always the token's subject, and the handler answers anyone else 404.
/// </summary>
public sealed class CertificateDocumentEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/certificates/{id:guid}/pdf", ReadAsync).RequireAuthorization();
    }

    private static async Task<Results<Ok<CertificationDocumentUrlDto>, ProblemHttpResult>> ReadAsync(
        Guid id,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var caller = CertificationCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Result<CertificationDocumentUrlDto, ApiError>.Failure(CertificateFailures.IdentityIncomplete()).ToHttpResult();
        }

        return (await dispatcher.QueryAsync(new GetCertificateDocument(id, caller.Value.Value), cancellationToken)).ToHttpResult();
    }
}
