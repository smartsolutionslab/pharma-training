namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A UTC calendar day, from 00:00 UTC to the next; the day a pharmacy's invitation mails are counted in.</summary>
public sealed record UtcDay : IValueObject<DateOnly>
{
    private UtcDay(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    /// <summary>The instant the day begins, 00:00 UTC.</summary>
    public Timestamp Start => Timestamp.From(new DateTimeOffset(Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    public static UtcDay From(DateOnly value) => new(value);

    /// <summary>The UTC calendar day the instant falls on.</summary>
    public static UtcDay Of(Timestamp instant)
    {
        Ensure.That(instant).IsNotNull();
        return new UtcDay(DateOnly.FromDateTime(instant.Value.UtcDateTime));
    }

    public UtcDay Next() => new(Value.AddDays(1));
}
