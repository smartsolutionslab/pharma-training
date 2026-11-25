using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

/// <summary>#391: a pharmacy that chose nothing keeps the default and says so; one that chose a period, five included, does not.</summary>
public class RetentionMappingsTests
{
    [Fact]
    public void Nothing_stored_is_the_default_of_five_years()
    {
        Option<PharmacyRetention>.None.ToCertificationRetentionPeriodDto().ShouldBe(new CertificationRetentionPeriodDto(5, true));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(5)]
    public void A_stored_period_is_its_years_and_no_default(int years)
    {
        var stored = Option<PharmacyRetention>.Some(PharmacyRetention.Chosen(PharmacyId.From(Guid.CreateVersion7()), RetentionPeriod.From(years)));

        stored.ToCertificationRetentionPeriodDto().ShouldBe(new CertificationRetentionPeriodDto(years, false));
    }
}
