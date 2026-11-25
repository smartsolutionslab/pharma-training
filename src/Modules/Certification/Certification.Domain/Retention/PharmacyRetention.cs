using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

/// <summary>The retention period a pharmacy chose (#391).</summary>
public sealed class PharmacyRetention : Entity<PharmacyId>
{
    private PharmacyRetention(PharmacyId id, RetentionPeriod period)
        : base(id)
    {
        Period = period;
    }

    public RetentionPeriod Period { get; }

    // stub for #391
    public static PharmacyRetention Chosen(PharmacyId pharmacy, RetentionPeriod period) => throw new NotImplementedException();
}
