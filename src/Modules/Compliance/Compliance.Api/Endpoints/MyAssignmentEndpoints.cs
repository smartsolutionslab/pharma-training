using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.ListMyAssignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// The learner's dashboard: the caller's own open and in-progress assignments in the pharmacy selected in the web app, a
/// page at a time, overdue first, each with the course version to open. Open to every signed-in person: whether the
/// caller is a member of that pharmacy is decided by the handler from Compliance's copy of their membership
/// (docs/adr/0003); no capability is needed to see one's own assignments. Mapped under the module's group, never at
/// <c>/me</c>, which the edge owns.
/// </summary>
public sealed class MyAssignmentEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/me/assignments", ListAsync)
            .RequireAuthorization();
    }

    private static async Task<Results<Ok<PagedResult<ComplianceMyAssignmentDto>>, ProblemHttpResult>> ListAsync(
        Guid? pharmacyId,
        Page? page,
        PageSize? pageSize,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var caller = ComplianceCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Refused(AssignmentFailures.IdentityIncomplete());
        }

        if (pharmacyId is not { } id)
        {
            return Refused(AssignmentFailures.PharmacyMissing());
        }

        // The empty Guid is no pharmacy Compliance holds; it is not found like any other.
        if (id == Guid.Empty)
        {
            return Refused(AssignmentFailures.PharmacyNotFound(id));
        }

        var query = new ListMyAssignments(caller.Value, PharmacyId.From(id), page ?? Page.First, pageSize ?? PageSize.Default);
        return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
    }

    private static Results<Ok<PagedResult<ComplianceMyAssignmentDto>>, ProblemHttpResult> Refused(ApiError error) =>
        Result<PagedResult<ComplianceMyAssignmentDto>, ApiError>.Failure(error).ToHttpResult();
}
