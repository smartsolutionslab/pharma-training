namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>A point in time, always UTC. The domain never reads a clock: callers pass it in.</summary>
public sealed record Timestamp : IValueObject<DateTimeOffset>, IComparable<Timestamp>
{
    // Resolved on first use, not in a static initialiser: an image without tzdata then fails DayInGermany() alone, not
    // every use of Timestamp with a TypeInitializationException.
    private static readonly Lazy<TimeZoneInfo> GermanTime = new(() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

    private Timestamp(DateTimeOffset value) => Value = value;

    public DateTimeOffset Value { get; }

    public static Timestamp From(DateTimeOffset value) => new(value.ToUniversalTime());

    public static bool operator <(Timestamp left, Timestamp right) => Compare(left, right) < 0;

    public static bool operator >(Timestamp left, Timestamp right) => Compare(left, right) > 0;

    public static bool operator <=(Timestamp left, Timestamp right) => Compare(left, right) <= 0;

    public static bool operator >=(Timestamp left, Timestamp right) => Compare(left, right) >= 0;

    /// <summary>The calendar day in German local time; deadlines are days, not instants.</summary>
    public CalendarDate DayInGermany() =>
        CalendarDate.From(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Value, GermanTime.Value).DateTime));

    /// <summary>The instant a calendar day begins in Germany: its midnight in German local time, as UTC.</summary>
    public static Timestamp StartOfDayInGermany(CalendarDate day)
    {
        Ensure.That(day).IsNotNull();
        var midnight = day.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new Timestamp(new DateTimeOffset(midnight, GermanTime.Value.GetUtcOffset(midnight)).ToUniversalTime());
    }

    public int CompareTo(Timestamp? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static int Compare(Timestamp? left, Timestamp? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
