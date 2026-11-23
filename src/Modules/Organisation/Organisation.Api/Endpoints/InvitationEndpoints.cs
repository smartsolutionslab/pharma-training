using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.AcceptInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RevokeInvitation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListPharmacyInvitations;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>
/// List a pharmacy's invitations, open or run out, invite an address, renew an invitation with a fresh link, or revoke
/// it, and accept one with the link's token. Any signed-in user may ask; whether they may is decided by their own
/// membership in the pharmacy (managing members), or, to accept, by the token and the account's verified address, not
/// by a realm role. Invite and renew each request an invitation mail, which is limited (docs/adr/0004): renewing
/// within 10 minutes of the invitation's latest mail, or mailing beyond the pharmacy's 50 a UTC day, answers 429 with
/// <c>Retry-After</c> and the instant as <c>retryAt</c>.
/// </summary>
public sealed class InvitationEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/pharmacies/{id:guid}/invitations", async (Guid id, Page? page, PageSize? pageSize, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<PagedResult<PharmacyInvitationDto>, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            if (Unknown(id) is { } unknown)
            {
                return Result<PagedResult<PharmacyInvitationDto>, ApiError>.Failure(unknown).ToHttpResult();
            }

            var query = new ListPharmacyInvitations(PharmacyId.From(id), person.Value, page ?? Page.First, pageSize ?? PageSize.Default);
            return (await dispatcher.QueryAsync(query, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();

        endpoints.MapPost("/pharmacies/{id:guid}/invitations", async (Guid id, InviteOrganisationMemberRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // The invitation has no resource of its own: its location is the pharmacy that holds it.
            string Location(Guid membership) => $"/organisation/pharmacies/{id}";

            // What the caller got wrong is refused before it becomes a value object, whose factory would throw on it.
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToCreatedHttpResult(Location);
            }

            // An empty id names nothing: answered as not found before anything else, as renew and revoke do.
            if (Unknown(id) is { } unknown)
            {
                return Result<Guid, ApiError>.Failure(unknown).ToCreatedHttpResult(Location);
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

            var command = new InviteMember(PharmacyId.From(id), person.Value, email, request.JobFunction.ToDomain(), request.Role.ToDomain());
            return (await dispatcher.SendAsync(command, cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

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
        }).RequireAuthorization()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

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

        endpoints.MapPost("/invitations/accept", async (AcceptOrganisationInvitationRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // Who accepts comes from the token alone, never from the body.
            var caller = CallerClaims.Of(user);
            if (!caller.HasValue)
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            if (!InvitationToken.TryFrom(request.Token, out var token))
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.InvalidInvitationToken()).ToHttpResult();
            }

            // The verified address is the only proof that the caller is the invitee; the use case refuses an unverified one.
            var email = AccountEmail.From(caller.Value.Email, CallerClaims.HasVerifiedEmail(user));
            var command = new AcceptInvitation(token, caller.Value.Person, caller.Value.Name, email);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToHttpResult();
        }).RequireAuthorization();
    }

    /// <summary>An empty id names nothing: answered as not found before its factory would refuse it.</summary>
    private static ApiError? Unknown(Guid pharmacy)
    {
        if (pharmacy == Guid.Empty)
        {
            return PharmacyFailures.NotFound(pharmacy);
        }

        return null;
    }

    /// <summary>An empty id names nothing: answered as not found before its factory would refuse it.</summary>
    private static ApiError? Unknown(Guid pharmacy, Guid membership)
    {
        if (Unknown(pharmacy) is { } unknown)
        {
            return unknown;
        }

        if (membership == Guid.Empty)
        {
            return PharmacyFailures.OpenInvitationNotFound(membership);
        }

        return null;
    }
}
