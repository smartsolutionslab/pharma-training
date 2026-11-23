using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListPharmacyMembers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// The assign wizard's member list: a pharmacy's active members, a page at a time, by name, optionally of one job
/// function. Open to every signed-in person: whether the caller may assign courses in that pharmacy, and so see its
/// members, is decided by the handler from Compliance's copy of their membership (docs/adr/0003).
/// </summary>
public sealed class MemberEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/pharmacies/{id:guid}/members", ListAsync)
            .RequireAuthorization();
    }

    private static async Task<Results<Ok<PagedResult<ComplianceMemberDto>>, ProblemHttpResult>> ListAsync(
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

        var query = new ListPharmacyMembers(
            PharmacyId.From(id),
            caller.Value,
            jobFunction?.ToDomain(),
            page ?? Page.First,
            pageSize ?? PageSize.Default);
        return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
    }

    private static Results<Ok<PagedResult<ComplianceMemberDto>>, ProblemHttpResult> Refused(ApiError error) =>
        Result<PagedResult<ComplianceMemberDto>, ApiError>.Failure(error).ToHttpResult();
}
