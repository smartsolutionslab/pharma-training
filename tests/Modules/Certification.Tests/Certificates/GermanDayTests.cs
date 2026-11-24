using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// Where a calendar day begins as an instant: at midnight in Germany, an hour before midnight UTC in winter and two in
/// summer, so a day and its instants agree with <see cref="Timestamp.DayInGermany"/>.
/// </summary>
public class GermanDayTests
{
    [Fact]
    public void A_winter_day_starts_an_hour_before_midnight_utc()
    {
        Timestamp.StartOfDayInGermany(CalendarDate.From(new DateOnly(2025, 1, 10)))
            .ShouldBe(Timestamp.From(new DateTimeOffset(2025, 1, 9, 23, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_summer_day_starts_two_hours_before_midnight_utc()
    {
        Timestamp.StartOfDayInGermany(CalendarDate.From(new DateOnly(2025, 7, 1)))
            .ShouldBe(Timestamp.From(new DateTimeOffset(2025, 6, 30, 22, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(2025, 1, 10)]
    [InlineData(2025, 3, 30)]
    [InlineData(2025, 7, 1)]
    [InlineData(2025, 10, 26)]
    public void The_start_of_a_day_lies_in_that_day_and_a_tick_before_it_in_the_day_before(int year, int month, int day)
    {
        var date = CalendarDate.From(new DateOnly(year, month, day));

        var start = Timestamp.StartOfDayInGermany(date);

        start.DayInGermany().ShouldBe(date);
        Timestamp.From(start.Value.AddTicks(-1)).DayInGermany().ShouldBe(CalendarDate.From(new DateOnly(year, month, day).AddDays(-1)));
    }

    [Fact]
    public void The_next_day_follows_across_a_month_and_a_year()
    {
        CalendarDate.From(new DateOnly(2025, 6, 30)).NextDay().ShouldBe(CalendarDate.From(new DateOnly(2025, 7, 1)));
        CalendarDate.From(new DateOnly(2025, 12, 31)).NextDay().ShouldBe(CalendarDate.From(new DateOnly(2026, 1, 1)));
    }
}
