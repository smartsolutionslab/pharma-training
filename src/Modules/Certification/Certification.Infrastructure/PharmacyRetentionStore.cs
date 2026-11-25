using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

// stub for #391
public sealed class PharmacyRetentionStore(CertificationDbContext context) : IPharmacyRetentionStore
{
    public Task<Option<PharmacyRetention>> FindAsync(PharmacyId pharmacy, CancellationToken cancellationToken)
    {
        _ = context;
        throw new NotImplementedException();
    }

    public Task SetAsync(PharmacyRetention retention, CancellationToken cancellationToken)
    {
        _ = context;
        throw new NotImplementedException();
    }
}
