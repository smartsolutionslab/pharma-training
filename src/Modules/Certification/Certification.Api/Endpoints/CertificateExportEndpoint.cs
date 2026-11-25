using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ExportPharmacyCertificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api.Endpoints;

/// <summary>
/// A pharmacy's certificates for a period as a ZIP, downloaded as an attachment (#52, docs/adr/0032). Open to every
/// signed-in person: whether they may is decided by the handler from Certification's copy of their membership in the
/// pharmacy and its current role (docs/adr/0003), not by a realm role. The archive is written into the response body as
/// it is assembled, and never stored.
/// </summary>
public sealed class CertificateExportEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/pharmacies/{id:guid}/certificates/export", ExportAsync)
            .RequireAuthorization()
            .Produces<Stream>(StatusCodes.Status200OK, CertificateExportArchive.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<PushStreamHttpResult, ProblemHttpResult>> ExportAsync(
        Guid id,
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        // A token naming no person is the empty caller, which the handler answers 403 identity-incomplete.
        var caller = CertificationCaller.PersonOf(user).Match(person => person.Value, () => Guid.Empty);
        var export = await dispatcher.QueryAsync(new ExportPharmacyCertificates(id, caller, from, to), cancellationToken);
        return export.ToStreamHttpResult(archive => TypedResults.Stream(
            body => archive.WriteAsync(body, cancellationToken),
            CertificateExportArchive.ContentType,
            archive.FileName));
    }
}
