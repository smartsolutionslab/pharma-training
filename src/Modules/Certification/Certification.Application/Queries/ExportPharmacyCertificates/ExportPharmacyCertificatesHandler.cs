using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ExportPharmacyCertificates;

/// <summary>
/// Chooses the certificates a manager exports for their pharmacy (#52, docs/adr/0032), checking in this order, the first
/// that fails answering: the caller is a person (403); the caller has an active membership in the pharmacy (404); every
/// such membership's current role grants the export (403); the period is valid (400); at most
/// <see cref="CertificateExportArchive.MaxCertificates"/> certificates are chosen (400). A certificate is chosen when its
/// holder was a member of the pharmacy at the instant it was issued, start inclusive and end exclusive, and its issue
/// day in Germany lies within the period. A certificate is chosen in the database; at most
/// <see cref="CertificateExportArchive.MaxCertificates"/> + 1 are read, so a refusal never loads more (#367). Nothing is
/// read from blob storage here: the archive reads each PDF as it is written into the response.
/// </summary>
public sealed class ExportPharmacyCertificatesHandler(IMembershipStore memberships, ICertificateStore certificates, IBlobReads blobs)
    : IQueryHandler<ExportPharmacyCertificates, Result<CertificateExportArchive, ApiError>>
{
    public async Task<Result<CertificateExportArchive, ApiError>> HandleAsync(ExportPharmacyCertificates query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Caller == Guid.Empty)
        {
            return Failure(CertificateFailures.IdentityIncomplete());
        }

        // The empty Guid is no pharmacy; nobody is a member of it.
        if (query.PharmacyId == Guid.Empty)
        {
            return Failure(ExportFailures.PharmacyNotFound(query.PharmacyId));
        }

        var pharmacy = PharmacyId.From(query.PharmacyId);
        var access = await CertificationAccess.RequireAsync(memberships, PersonId.From(query.Caller), pharmacy, Capability.ExportCertificates, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var period = ExportPeriod.Of(query.From, query.To);
        if (period.IsFailure)
        {
            return Failure(period.Error);
        }

        var chosen = await certificates.IssuedInPharmacyAsync(pharmacy, period.Value.Start, period.Value.End, CertificateExportArchive.MaxCertificates + 1, cancellationToken);
        if (chosen.Count > CertificateExportArchive.MaxCertificates)
        {
            return Failure(ExportFailures.TooLarge());
        }

        return Success(new CertificateExportArchive(period.Value, chosen, blobs));
    }
}
