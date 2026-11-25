using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetRetentionPeriod;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>
/// How many years a pharmacy keeps a certificate in its view (#391, docs/adr/0039), read and set by its manager. Open to
/// every signed-in person: whether they may is decided by the handler from Certification's copy of their membership in
/// the pharmacy and its current role (docs/adr/0003), not by a realm role. The period's range is checked here, where the
/// request's years become a <see cref="RetentionPeriod"/>.
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
        var caller = CertificationCaller.PersonOf(user);
        if (!caller.HasValue)
        {
            return Refused(CertificateFailures.IdentityIncomplete());
        }

        // The empty Guid is no pharmacy; nobody is a member of it.
        if (id == Guid.Empty)
        {
            return Refused(ExportFailures.PharmacyNotFound(id));
        }

        Ensure.That(request).IsNotNull();
        if (request.Years is not { } years || !RetentionPeriod.TryFrom(years, out var period))
        {
            return Refused(RetentionFailures.InvalidPeriod());
        }

        var command = new SetRetentionPeriod(PharmacyId.From(id), period, caller.Value);
        return (await dispatcher.SendAsync(command, cancellationToken)).ToNoContentHttpResult();
    }

    private static Results<NoContent, ProblemHttpResult> Refused(ApiError error) =>
        Result<Unit, ApiError>.Failure(error).ToNoContentHttpResult();
}
