using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Retention;

/// <summary>#391: a pharmacy keeps certificates for a whole number of years from 2 to 30; 5 when it chose none.</summary>
public class RetentionPeriodTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    public void A_period_from_two_to_thirty_years_is_that_many_years(int years)
    {
        RetentionPeriod.From(years).Value.ShouldBe(years);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_period_outside_two_to_thirty_years_is_refused(int years)
    {
        Should.Throw<GuardException>(() => RetentionPeriod.From(years));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    public void A_callers_period_within_the_range_is_accepted(int years)
    {
        RetentionPeriod.TryFrom(years, out var period).ShouldBeTrue();

        period.ShouldBe(RetentionPeriod.From(years));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    public void A_callers_period_outside_the_range_is_not(int years)
    {
        RetentionPeriod.TryFrom(years, out var period).ShouldBeFalse();

        period.ShouldBeNull();
    }

    [Fact]
    public void The_default_is_five_years()
    {
        RetentionPeriod.Default.ShouldBe(RetentionPeriod.From(5));
        RetentionPeriod.Default.Value.ShouldBe(5);
    }

    [Fact]
    public void A_period_reads_as_its_years()
    {
        RetentionPeriod.From(7).ToString().ShouldBe("7");
    }
}
