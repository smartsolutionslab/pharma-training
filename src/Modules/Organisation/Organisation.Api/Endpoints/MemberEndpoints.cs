using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.ChangeMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.EndMembership;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyMembers;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>
/// List a pharmacy's active members, end a membership, or change its job function and role. Any signed-in user may
/// ask; whether they may is decided by their own membership in the pharmacy (its management only), not by a realm role.
/// </summary>
public sealed class MemberEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/pharmacies/{id:guid}/members", async (Guid id, Page? page, PageSize? pageSize, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<PagedResult<PharmacyMemberDto>, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            // An empty id names nothing: answered as not found, as the other pharmacy endpoints do, before its factory would refuse it.
            if (id == Guid.Empty)
            {
                return Result<PagedResult<PharmacyMemberDto>, ApiError>.Failure(PharmacyFailures.NotFound(id)).ToHttpResult();
            }

            var query = new ListPharmacyMembers(PharmacyId.From(id), person.Value, page ?? Page.First, pageSize ?? PageSize.Default);
            return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();

        endpoints.MapDelete("/pharmacies/{id:guid}/members/{membershipId:guid}", async (Guid id, Guid membershipId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<Unit, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            if (Unknown(id, membershipId) is { } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown).ToHttpResult();
            }

            var command = new EndMembership(PharmacyId.From(id), MembershipId.From(membershipId), person.Value);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();

        endpoints.MapPatch("/pharmacies/{id:guid}/members/{membershipId:guid}", async (Guid id, Guid membershipId, ChangeOrganisationMembershipRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<Unit, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            // The enums' converters also read numbers, so an undefined one is the caller's mistake, never a mapping's bug.
            if (!Enum.IsDefined(request.JobFunction))
            {
                return Result<Unit, ApiError>.Failure(PharmacyFailures.InvalidJobFunction()).ToHttpResult();
            }

            if (!Enum.IsDefined(request.Role))
            {
                return Result<Unit, ApiError>.Failure(PharmacyFailures.InvalidMembershipRole()).ToHttpResult();
            }

            if (Unknown(id, membershipId) is { } unknown)
            {
                return Result<Unit, ApiError>.Failure(unknown).ToHttpResult();
            }

            var command = new ChangeMembership(
                PharmacyId.From(id),
                MembershipId.From(membershipId),
                person.Value,
                request.JobFunction.ToDomain(),
                request.Role.ToDomain());
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();
    }

    /// <summary>An empty id names nothing: answered as not found, as the other pharmacy endpoints do, before its factory would refuse it.</summary>
    private static ApiError? Unknown(Guid pharmacy, Guid membership)
    {
        if (pharmacy == Guid.Empty)
        {
            return PharmacyFailures.NotFound(pharmacy);
        }

        if (membership == Guid.Empty)
        {
            return PharmacyFailures.MembershipNotFound(membership);
        }

        return null;
    }
}
