using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>The retention period each pharmacy chose (#391); implemented in the module's Infrastructure project.</summary>
public interface IPharmacyRetentionStore
{
    /// <summary>The period the pharmacy chose, read without tracking; none while it chose none.</summary>
    Task<Option<PharmacyRetention>> FindAsync(PharmacyId pharmacy, CancellationToken cancellationToken);

    /// <summary>Stores the pharmacy's period in one atomic upsert, replacing the one it held.</summary>
    Task SetAsync(PharmacyRetention retention, CancellationToken cancellationToken);
}
