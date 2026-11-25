using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

/// <summary>#391: the period a pharmacy chose, kept under the pharmacy's id.</summary>
public class PharmacyRetentionTests
{
    [Fact]
    public void A_chosen_period_keeps_its_pharmacy_and_years()
    {
        var pharmacy = PharmacyId.From(Guid.CreateVersion7());

        var retention = PharmacyRetention.Chosen(pharmacy, RetentionPeriod.From(7));

        retention.Id.ShouldBe(pharmacy);
        retention.Period.ShouldBe(RetentionPeriod.From(7));
    }

    [Fact]
    public void A_period_needs_its_pharmacy()
    {
        Should.Throw<GuardException>(() => PharmacyRetention.Chosen(null!, RetentionPeriod.Default));
    }

    [Fact]
    public void A_pharmacy_needs_its_period()
    {
        Should.Throw<GuardException>(() => PharmacyRetention.Chosen(PharmacyId.From(Guid.CreateVersion7()), null!));
    }
}
