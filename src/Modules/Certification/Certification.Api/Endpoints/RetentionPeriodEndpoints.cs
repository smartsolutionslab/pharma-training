using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetRetentionPeriod;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>
/// How many years a pharmacy keeps a certificate in its view (#391, docs/adr/0039), read and set by its manager. Open to
/// every signed-in person: whether they may is decided by the handler from Certification's copy of their membership in
/// the pharmacy and its current role (docs/adr/0003), not by a realm role. The handler checks who is calling before it
/// checks the years, so a caller who may not set the period learns nothing from what they sent (#422).
/// </summary>
public sealed class RetentionPeriodEndpoints : IEndpoint
{
    private const string Route = "/pharmacies/{id:guid}/retention-period";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet(Route, GetAsync)
            .RequireAuthorization()
            .Produces<CertificationRetentionPeriodDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPut(Route, SetAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<CertificationRetentionPeriodDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        // A token naming no person is the empty caller, which the handler answers 403 identity-incomplete.
        var caller = CertificationCaller.PersonOf(user).Match(person => person.Value, () => Guid.Empty);
        return (await dispatcher.QueryAsync(new GetRetentionPeriod(id, caller), cancellationToken)).ToHttpResult();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetAsync(
        Guid id,
        SetCertificationRetentionPeriodRequest request,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Ensure.That(request).IsNotNull();
        var command = new SetRetentionPeriod(PharmacyOf(id), RequestedRetentionYears.From(request.Years), CertificationCaller.PersonOf(user));
        return (await dispatcher.SendAsync(command, cancellationToken)).ToNoContentHttpResult();
    }

    /// <summary>The pharmacy the route names; the empty Guid names none, which the handler answers 404.</summary>
    private static Option<PharmacyId> PharmacyOf(Guid id)
    {
        if (id == Guid.Empty)
        {
            return Option<PharmacyId>.None;
        }

        return Option<PharmacyId>.Some(PharmacyId.From(id));
    }
}
