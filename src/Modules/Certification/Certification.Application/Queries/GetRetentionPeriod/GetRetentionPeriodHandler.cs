using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetRetentionPeriod;

/// <summary>
/// The pharmacy's retention period (#391, docs/adr/0039), checking in this order, the first that fails answering: the
/// caller is a person (403); the caller has an active membership in the pharmacy (404); every such membership's current
/// role grants <see cref="Capability.ManageRetention"/> (403). A pharmacy that chose none keeps the default of five years.
/// </summary>
public sealed class GetRetentionPeriodHandler(IMembershipStore memberships, IPharmacyRetentionStore retention)
    : IQueryHandler<GetRetentionPeriod, Result<CertificationRetentionPeriodDto, ApiError>>
{
    public async Task<Result<CertificationRetentionPeriodDto, ApiError>> HandleAsync(GetRetentionPeriod query, CancellationToken cancellationToken)
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
        var access = await CertificationAccess.RequireAsync(memberships, PersonId.From(query.Caller), pharmacy, Capability.ManageRetention, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        return Success((await retention.FindAsync(pharmacy, cancellationToken)).ToCertificationRetentionPeriodDto());
    }
}
