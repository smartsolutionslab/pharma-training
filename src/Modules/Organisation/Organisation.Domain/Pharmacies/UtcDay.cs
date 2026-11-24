namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>A UTC calendar day, from 00:00 UTC to the next; the day a pharmacy's or a sender's invitation mails are counted in.</summary>
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

    /// <summary>
    /// The stored day of a tally of invitation mails when it still counts: today, or tomorrow, so a request racing
    /// 00:00 UTC adds to the new day's tally instead of restarting the old day's. A stored day before today, or beyond
    /// tomorrow (every instance reads the database's clock, so only a hand-edited row or a database clock that moved
    /// back, by a failover or a restore, stores one), counts for nothing, and today's tally starts afresh; it never
    /// locks invitation mail (docs/adr/0004).
    /// </summary>
    public static Option<UtcDay> CountingDay(Timestamp now, UtcDay? stored)
    {
        Ensure.That(now).IsNotNull();

        var today = Of(now);
        if (stored is not null && stored.Value.DayNumber - today.Value.DayNumber is 0 or 1)
        {
            return Option<UtcDay>.Some(stored);
        }

        return Option<UtcDay>.None;
    }

    /// <summary>
    /// The stored day of a tally when it lies beyond tomorrow, which only a hand-edited row, or a database clock that
    /// moved back (a failover or a restore), stores; none otherwise.
    /// </summary>
    public static Option<UtcDay> BeyondTomorrow(Timestamp now, UtcDay? stored)
    {
        Ensure.That(now).IsNotNull();

        if (stored is not null && stored.Value.DayNumber - Of(now).Value.DayNumber > 1)
        {
            return Option<UtcDay>.Some(stored);
        }

        return Option<UtcDay>.None;
    }
}
