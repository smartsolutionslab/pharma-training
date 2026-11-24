namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>An instant, kept in UTC so that two spellings of one instant are equal.</summary>
public sealed record Timestamp : IValueObject<DateTimeOffset>, IComparable<Timestamp>
{
    private Timestamp(DateTimeOffset value) => Value = value;

    public DateTimeOffset Value { get; }

    public static Timestamp From(DateTimeOffset value) => new(value.ToUniversalTime());

    public Timestamp PlusDays(int days) => From(Value.AddDays(days));

    public Timestamp Plus(TimeSpan duration) => From(Value.Add(duration));

    public int CompareTo(Timestamp? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(Timestamp left, Timestamp right) => Compare(left, right) < 0;

    public static bool operator >(Timestamp left, Timestamp right) => Compare(left, right) > 0;

    public static bool operator <=(Timestamp left, Timestamp right) => Compare(left, right) <= 0;

    public static bool operator >=(Timestamp left, Timestamp right) => Compare(left, right) >= 0;

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static int Compare(Timestamp? left, Timestamp? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
