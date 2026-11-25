using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RequestErasure;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetErasure;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>
/// Request a person's erasure (GDPR Art. 17, #115) and read it back from the erasure log. Support staff only, through
/// the policy <see cref="OrganisationPolicyNames.ErasePersons"/>; the requester is the caller.
/// </summary>
public sealed class ErasureEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/erasures", async (RequestOrganisationErasureRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var caller = CallerClaims.PersonOf(user);
            if (!caller.HasValue)
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToCreatedHttpResult(Location);
            }

            // The empty Guid is no person's: PersonId.From refuses it, which the host answers with 400 invalid-argument.
            var command = new RequestErasure(PersonId.From(request.PersonId), caller.Value);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization(OrganisationPolicyNames.ErasePersons);

        endpoints.MapGet("/erasures/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new GetErasure(id), cancellationToken)).ToHttpResult())
            .RequireAuthorization(OrganisationPolicyNames.ErasePersons);
    }

    private static string Location(Guid id) => $"/organisation/erasures/{id}";
}
