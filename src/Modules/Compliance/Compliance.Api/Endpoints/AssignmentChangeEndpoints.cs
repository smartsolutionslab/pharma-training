using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.CancelAssignment;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Commands.ChangeAssignmentDueDate;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Queries.GetAssignment;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api.Endpoints;

/// <summary>
/// A manager reads one assignment of their pharmacy with its history, changes its due date or withdraws it (#30). Open
/// to every signed-in person: whether the caller may do so in that pharmacy is decided by the handler from Compliance's
/// copy of their membership (docs/adr/0003). A change or withdrawal returns nothing, so it answers 200 with
/// <see cref="Unit"/>.
/// </summary>
public sealed class AssignmentChangeEndpoints : IEndpoint
{
    private const string Route = "/pharmacies/{id:guid}/assignments/{assignmentId:guid}";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet(Route, GetAsync).RequireAuthorization();
        endpoints.MapPatch(Route, ChangeAsync).RequireAuthorization();
        endpoints.MapDelete(Route, WithdrawAsync).RequireAuthorization();
    }

    private static async Task<Results<Ok<ComplianceAssignmentDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        Guid assignmentId,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var target = Target(id, assignmentId, user);
        if (target.IsFailure)
        {
            return Result<ComplianceAssignmentDto, ApiError>.Failure(target.Error).ToHttpResult();
        }

        var (pharmacy, assignment, caller) = target.Value;
        return (await dispatcher.QueryAsync(new GetAssignment(pharmacy, assignment, caller), cancellationToken)).ToHttpResult();
    }

    private static async Task<Results<Ok<Unit>, ProblemHttpResult>> ChangeAsync(
        Guid id,
        Guid assignmentId,
        ChangeComplianceAssignmentRequest request,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var target = Target(id, assignmentId, user);
        if (target.IsFailure)
        {
            return Refused(target.Error);
        }

        // A body without a due date is not read as 0001-01-01 and then told the date is past.
        if (request.DueDate is not { } dueDate)
        {
            return Refused(AssignmentFailures.DueDateMissing());
        }

        // What the caller got wrong is refused before it becomes a value object, whose factory would throw on it.
        ChangeReason? reason = null;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            if (!ChangeReason.TryFrom(request.Reason, out var given))
            {
                return Refused(AssignmentFailures.InvalidChangeReason());
            }

            reason = given;
        }

        var (pharmacy, assignment, caller) = target.Value;
        var command = new ChangeAssignmentDueDate(pharmacy, assignment, DueDate.From(dueDate), reason, caller);
        return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
    }

    private static async Task<Results<Ok<Unit>, ProblemHttpResult>> WithdrawAsync(
        Guid id,
        Guid assignmentId,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var target = Target(id, assignmentId, user);
        if (target.IsFailure)
        {
            return Refused(target.Error);
        }

        var (pharmacy, assignment, caller) = target.Value;
        return (await dispatcher.SendAsync(new CancelAssignment(pharmacy, assignment, caller), cancellationToken)).ToHttpResult();
    }

    /// <summary>
    /// The caller and the assignment the route names. A caller without a usable subject is refused first; the empty Guid
    /// is no pharmacy and no assignment Compliance holds, and each is not found like any other.
    /// </summary>
    private static Result<(PharmacyId Pharmacy, AssignmentId Assignment, PersonId Caller), ApiError> Target(Guid id, Guid assignmentId, ClaimsPrincipal user)
    {
        var caller = ComplianceCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Failure(AssignmentFailures.IdentityIncomplete());
        }

        if (id == Guid.Empty)
        {
            return Failure(AssignmentFailures.PharmacyNotFound(id));
        }

        if (assignmentId == Guid.Empty)
        {
            return Failure(AssignmentFailures.AssignmentNotFound(assignmentId));
        }

        return Success((PharmacyId.From(id), AssignmentId.From(assignmentId), caller.Value));
    }

    private static Results<Ok<Unit>, ProblemHttpResult> Refused(ApiError error) =>
        Result<Unit, ApiError>.Failure(error).ToHttpResult();
}
