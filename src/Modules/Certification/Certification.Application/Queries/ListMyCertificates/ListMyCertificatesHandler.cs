using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.ListMyCertificates;

/// <summary>
/// Lists a page of the caller's own certificates, newest first, each valid or expired on today's German day (#295).
/// Only the caller's are counted and listed, whether or not the membership they were earned in still runs; a caller
/// who is no person is forbidden. The store pages the certificates and they are mapped here, since the validity
/// depends on today.
/// </summary>
public sealed class ListMyCertificatesHandler(ICertificateStore certificates, TimeProvider time)
    : IQueryHandler<ListMyCertificates, Result<PagedResult<HeldCertificateDto>, ApiError>>
{
    public async Task<Result<PagedResult<HeldCertificateDto>, ApiError>> HandleAsync(ListMyCertificates query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Caller == Guid.Empty)
        {
            return Failure(CertificateFailures.IdentityIncomplete());
        }

        var (items, totalCount) = await certificates.ListForHolderAsync(PersonId.From(query.Caller), query.Page, query.PageSize, cancellationToken);
        var today = Timestamp.From(time.GetUtcNow()).DayInGermany();
        return Success(new PagedResult<HeldCertificateDto>(
            [.. items.Select(certificate => certificate.ToHeldCertificateDto(today))],
            totalCount,
            query.Page.Value,
            query.PageSize.Value));
    }
}
