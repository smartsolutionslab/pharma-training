namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>A day of the calendar, without a time or a zone.</summary>
public sealed record CalendarDate : IValueObject<DateOnly>, IComparable<CalendarDate>
{
    private CalendarDate(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static CalendarDate From(DateOnly value) => new(value);

    public static bool operator <(CalendarDate left, CalendarDate right) => Compare(left, right) < 0;

    public static bool operator >(CalendarDate left, CalendarDate right) => Compare(left, right) > 0;

    public static bool operator <=(CalendarDate left, CalendarDate right) => Compare(left, right) <= 0;

    public static bool operator >=(CalendarDate left, CalendarDate right) => Compare(left, right) >= 0;

    /// <summary>The day the interval later; a day the shorter month lacks becomes its last day.</summary>
    public CalendarDate Plus(RecurrenceInterval interval) => new(Value.AddMonths(interval.Value));

    /// <summary>The day after this one.</summary>
    public CalendarDate NextDay() => new(Value.AddDays(1));

    public int CompareTo(CalendarDate? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static int Compare(CalendarDate? left, CalendarDate? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
