using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;

/// <summary>
/// The days an export covers (#52): from and to, both given, both inclusive, to not before from and at most
/// <see cref="MaxYears"/> years after it. As instants it runs from <see cref="Start"/>, the start of the first day in
/// Germany, up to but not including <see cref="End"/>, the start of the day after the last.
/// </summary>
public sealed record ExportPeriod
{
    public const int MaxYears = 5;

    private ExportPeriod(CalendarDate from, CalendarDate to)
    {
        From = from;
        To = to;
    }

    public CalendarDate From { get; }

    public CalendarDate To { get; }

    /// <summary>The first instant of the period: midnight in Germany at the start of <see cref="From"/>.</summary>
    public Timestamp Start => Timestamp.StartOfDayInGermany(From);

    /// <summary>The first instant after the period: midnight in Germany at the start of the day after <see cref="To"/>.</summary>
    public Timestamp End => Timestamp.StartOfDayInGermany(To.NextDay());

    public static Result<ExportPeriod, ApiError> Of(DateOnly? from, DateOnly? to)
    {
        if (from is not { } first || to is not { } last || last < first || last > first.AddYears(MaxYears))
        {
            return Failure(ExportFailures.InvalidPeriod());
        }

        return Success(new ExportPeriod(CalendarDate.From(first), CalendarDate.From(last)));
    }
}
