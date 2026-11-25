using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

/// <summary>
/// The retention period a pharmacy chose (#391, docs/adr/0039), kept under the pharmacy's id. A record Certification
/// keeps, like a membership: no aggregate root and no domain events. A pharmacy without one keeps the default.
/// </summary>
public sealed class PharmacyRetention : Entity<PharmacyId>
{
    private PharmacyRetention(PharmacyId id, RetentionPeriod period)
        : base(id)
    {
        Period = period;
    }

    public RetentionPeriod Period { get; }

    public static PharmacyRetention Chosen(PharmacyId pharmacy, RetentionPeriod period)
    {
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(period).IsNotNull();
        return new PharmacyRetention(pharmacy, period);
    }
}
