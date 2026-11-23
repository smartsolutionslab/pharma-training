using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetComplianceMatrix;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// The team matrix: a pharmacy's active members, a page at a time, by the courses assigned there, with the compliance
/// rate, optionally of one job function. Open to every signed-in person: whether the caller may view the team's
/// compliance in that pharmacy is decided by the handler from Compliance's copy of their membership (docs/adr/0003).
/// </summary>
public sealed class MatrixEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/pharmacies/{id:guid}/matrix", GetAsync)
            .RequireAuthorization();
    }

    private static async Task<Results<Ok<ComplianceMatrixDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        Page? page,
        PageSize? pageSize,
        ComplianceJobFunction? jobFunction,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var caller = ComplianceCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Refused(AssignmentFailures.IdentityIncomplete());
        }

        // The empty Guid is no pharmacy Compliance holds; it is not found like any other.
        if (id == Guid.Empty)
        {
            return Refused(AssignmentFailures.PharmacyNotFound(id));
        }

        // A number binds to the enum whether or not it names one; an undefined one is the caller's mistake, not a bug.
        if (jobFunction is { } asked && !Enum.IsDefined(asked))
        {
            return Refused(AssignmentFailures.InvalidJobFunction());
        }

        var query = new GetComplianceMatrix(
            PharmacyId.From(id),
            caller.Value,
            jobFunction?.ToDomain(),
            page ?? Page.First,
            pageSize ?? PageSize.Default);
        return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
    }

    private static Results<Ok<ComplianceMatrixDto>, ProblemHttpResult> Refused(ApiError error) =>
        Result<ComplianceMatrixDto, ApiError>.Failure(error).ToHttpResult();
}
