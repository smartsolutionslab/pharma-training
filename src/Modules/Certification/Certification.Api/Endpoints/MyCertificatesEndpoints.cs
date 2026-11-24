using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ListMyCertificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>
/// The caller's own certificates, a page at a time and newest first (#295). Open to every signed-in person, without a
/// policy or a membership: certificates outlive the membership they were earned in. The holder is the token's subject.
/// </summary>
public sealed class MyCertificatesEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/me/certificates", ListAsync).RequireAuthorization();
    }

    private static async Task<Results<Ok<PagedResult<CertificateDto>>, ProblemHttpResult>> ListAsync(
        Page? page,
        PageSize? pageSize,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var caller = CertificationCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Result<PagedResult<CertificateDto>, ApiError>.Failure(CertificateFailures.IdentityIncomplete()).ToHttpResult();
        }

        var query = new ListMyCertificates(caller.Value.Value, page ?? Page.First, pageSize ?? PageSize.Default);
        return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
    }
}
