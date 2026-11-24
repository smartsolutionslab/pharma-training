using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetCertificateDocument;

/// <summary>
/// Signs a read URL on a certificate's stored PDF for its holder (#50, docs/adr/0002, docs/adr/0027), checking in this
/// order, the first that fails answering: the caller is a person (403); a certificate of that person has the id, with
/// someone else's not found like an unknown one, so nobody learns which ids exist (404); its document is stored (409).
/// The URL lives five minutes and is signed inline as <c>application/pdf</c>, so the bytes go from storage to the
/// browser and never through the service. Nothing is signed for a refused caller.
/// </summary>
public sealed class GetCertificateDocumentHandler(ICertificateStore certificates, IBlobUrls urls)
    : IQueryHandler<GetCertificateDocument, Result<CertificationDocumentUrlDto, ApiError>>
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    public async Task<Result<CertificationDocumentUrlDto, ApiError>> HandleAsync(GetCertificateDocument query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Caller == Guid.Empty)
        {
            return Failure(CertificateFailures.IdentityIncomplete());
        }

        // The empty Guid is no certificate; it is not found like any other.
        if (query.CertificateId == Guid.Empty)
        {
            return Failure(CertificateFailures.NotFound(query.CertificateId));
        }

        var found = await certificates.FindAsync(CertificateId.From(query.CertificateId), cancellationToken);
        if (!found.HasValue || found.Value.Holder != PersonId.From(query.Caller))
        {
            return Failure(CertificateFailures.NotFound(query.CertificateId));
        }

        var certificate = found.Value;
        if (certificate.Document != DocumentState.Stored || certificate.DocumentLocation is null)
        {
            return Failure(CertificateFailures.DocumentNotReady(query.CertificateId));
        }

        var url = await urls.CreateReadUrlAsync(
            BlobName.From(certificate.DocumentLocation.Value),
            BlobUrlLifetime.From(UrlLifetime),
            BlobReadHeaders.Inline(CertificateDocuments.ContentType),
            cancellationToken);
        return Success(url.ToDto());
    }
}
