using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RevokeInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>
/// Invite an address, renew an invitation with a fresh link, or revoke it. Any signed-in user may ask; whether they may
/// is decided by their own membership in the pharmacy (managing members), not by a realm role.
/// </summary>
public sealed class InvitationEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/pharmacies/{id:guid}/invitations", async (Guid id, InviteOrganisationMemberRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            string Location(Guid membership) => $"/organisation/pharmacies/{id}/invitations/{membership}";

            // What the caller got wrong is refused before it becomes a value object, whose factory would throw on it.
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToCreatedHttpResult(Location);
            }

            if (!EmailAddress.TryFrom(request.Email, out var email))
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.InvalidEmail()).ToCreatedHttpResult(Location);
            }

            // The enums' converters also read numbers, so an undefined one is the caller's mistake, never a mapping's bug.
            if (!Enum.IsDefined(request.JobFunction))
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.InvalidJobFunction()).ToCreatedHttpResult(Location);
            }

            if (!Enum.IsDefined(request.Role))
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.InvalidMembershipRole()).ToCreatedHttpResult(Location);
            }

            // An empty id names nothing: answered as not found, as the other pharmacy endpoints do.
            if (id == Guid.Empty)
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.NotFound(id)).ToCreatedHttpResult(Location);
            }

            var command = new InviteMember(PharmacyId.From(id), person.Value, email, request.JobFunction.ToDomain(), request.Role.ToDomain());
            return (await dispatcher.SendAsync(command, cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization();

        endpoints.MapPost("/pharmacies/{id:guid}/invitations/{membershipId:guid}/renew", async (Guid id, Guid membershipId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
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

            var command = new RenewInvitation(PharmacyId.From(id), MembershipId.From(membershipId), person.Value);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();

        endpoints.MapDelete("/pharmacies/{id:guid}/invitations/{membershipId:guid}", async (Guid id, Guid membershipId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
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

            var command = new RevokeInvitation(PharmacyId.From(id), MembershipId.From(membershipId), person.Value);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();
    }

    /// <summary>An empty id names nothing: answered as not found before its factory would refuse it.</summary>
    private static ApiError? Unknown(Guid pharmacy, Guid membership)
    {
        if (pharmacy == Guid.Empty)
        {
            return PharmacyFailures.NotFound(pharmacy);
        }

        if (membership == Guid.Empty)
        {
            return PharmacyFailures.OpenInvitationNotFound(membership);
        }

        return null;
    }
}
