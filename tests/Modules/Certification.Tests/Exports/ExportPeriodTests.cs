using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// The days an export covers (#52): from and to, both given, both inclusive, to not before from and at most five years
/// after it. As instants it runs from the start of the first day in Germany to the start of the day after the last.
/// </summary>
public class ExportPeriodTests
{
    [Fact]
    public void A_period_keeps_its_days()
    {
        var period = ExportPeriod.Of(new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30));

        period.IsSuccess.ShouldBeTrue();
        period.Value.From.ShouldBe(CalendarDate.From(new DateOnly(2025, 5, 1)));
        period.Value.To.ShouldBe(CalendarDate.From(new DateOnly(2025, 6, 30)));
    }

    [Fact]
    public void A_single_day_is_a_period()
    {
        ExportPeriod.Of(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Exactly_five_years_is_a_period()
    {
        ExportPeriod.MaxYears.ShouldBe(5);
        ExportPeriod.Of(new DateOnly(2021, 1, 1), new DateOnly(2026, 1, 1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Five_years_from_a_leap_day_end_on_the_last_day_of_february()
    {
        ExportPeriod.Of(new DateOnly(2020, 2, 29), new DateOnly(2025, 2, 28)).IsSuccess.ShouldBeTrue();
        ShouldBeInvalid(ExportPeriod.Of(new DateOnly(2020, 2, 29), new DateOnly(2025, 3, 1)));
    }

    [Fact]
    public void Five_years_and_a_day_is_refused()
    {
        ShouldBeInvalid(ExportPeriod.Of(new DateOnly(2021, 1, 1), new DateOnly(2026, 1, 2)));
    }

    [Fact]
    public void An_end_before_the_start_is_refused()
    {
        ShouldBeInvalid(ExportPeriod.Of(new DateOnly(2025, 6, 2), new DateOnly(2025, 6, 1)));
    }

    [Fact]
    public void A_missing_start_or_end_is_refused()
    {
        ShouldBeInvalid(ExportPeriod.Of(null, new DateOnly(2025, 6, 1)));
        ShouldBeInvalid(ExportPeriod.Of(new DateOnly(2025, 6, 1), null));
        ShouldBeInvalid(ExportPeriod.Of(null, null));
    }

    [Fact]
    public void In_winter_time_it_runs_from_midnight_in_germany_to_midnight_after_the_last_day()
    {
        var period = ExportPeriod.Of(new DateOnly(2025, 1, 10), new DateOnly(2025, 1, 20)).Value;

        period.Start.ShouldBe(Timestamp.From(new DateTimeOffset(2025, 1, 9, 23, 0, 0, TimeSpan.Zero)));
        period.End.ShouldBe(Timestamp.From(new DateTimeOffset(2025, 1, 20, 23, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void In_summer_time_it_runs_from_midnight_in_germany_to_midnight_after_the_last_day()
    {
        var period = ExportPeriod.Of(new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30)).Value;

        period.Start.ShouldBe(Timestamp.From(new DateTimeOffset(2025, 4, 30, 22, 0, 0, TimeSpan.Zero)));
        period.End.ShouldBe(Timestamp.From(new DateTimeOffset(2025, 6, 30, 22, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_period_ending_on_the_day_summer_time_begins_ends_at_midnight_summer_time()
    {
        // Clocks go forward at 02:00 on 30 March 2025: that day starts in winter time, the next in summer time.
        var period = ExportPeriod.Of(new DateOnly(2025, 3, 30), new DateOnly(2025, 3, 30)).Value;

        period.Start.ShouldBe(Timestamp.From(new DateTimeOffset(2025, 3, 29, 23, 0, 0, TimeSpan.Zero)));
        period.End.ShouldBe(Timestamp.From(new DateTimeOffset(2025, 3, 30, 22, 0, 0, TimeSpan.Zero)));
    }

    private static void ShouldBeInvalid(Result<ExportPeriod, ApiError> period)
    {
        period.IsFailure.ShouldBeTrue();
        period.Error.Code.ShouldBe("certification.invalid-export-period");
        period.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
    }
}
