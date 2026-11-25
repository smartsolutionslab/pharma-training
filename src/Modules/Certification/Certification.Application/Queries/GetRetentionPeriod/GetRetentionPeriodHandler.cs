using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Queries.GetRetentionPeriod;

// stub for #391
public sealed class GetRetentionPeriodHandler(IMembershipStore memberships, IPharmacyRetentionStore retention)
    : IQueryHandler<GetRetentionPeriod, Result<CertificationRetentionPeriodDto, ApiError>>
{
    public Task<Result<CertificationRetentionPeriodDto, ApiError>> HandleAsync(GetRetentionPeriod query, CancellationToken cancellationToken)
    {
        _ = memberships;
        _ = retention;
        throw new NotImplementedException();
    }
}
