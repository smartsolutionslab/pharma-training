namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>A point in time, always UTC. The domain never reads a clock: callers pass it in.</summary>
public sealed record Timestamp : IValueObject<DateTimeOffset>, IComparable<Timestamp>
{
    private static readonly TimeZoneInfo GermanTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private Timestamp(DateTimeOffset value) => Value = value;

    public DateTimeOffset Value { get; }

    public static Timestamp From(DateTimeOffset value) => new(value.ToUniversalTime());

    public static bool operator <(Timestamp left, Timestamp right) => Compare(left, right) < 0;

    public static bool operator >(Timestamp left, Timestamp right) => Compare(left, right) > 0;

    public static bool operator <=(Timestamp left, Timestamp right) => Compare(left, right) <= 0;

    public static bool operator >=(Timestamp left, Timestamp right) => Compare(left, right) >= 0;

    /// <summary>The calendar day in German local time; deadlines are days, not instants.</summary>
    public CalendarDate DayInGermany() =>
        CalendarDate.From(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Value, GermanTime).DateTime));

    public int CompareTo(Timestamp? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static int Compare(Timestamp? left, Timestamp? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
