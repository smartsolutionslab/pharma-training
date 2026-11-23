namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>The last day on which the course counts as done in time.</summary>
public sealed record DueDate : IValueObject<DateOnly>
{
    private DueDate(DateOnly value) => Value = value;

    public DateOnly Value { get; }

    public static DueDate From(DateOnly value) => new(value);

    public static DueDate On(CalendarDate date)
    {
        Ensure.That(date).IsNotNull();
        return new DueDate(date.Value);
    }

    public CalendarDate AsDate() => CalendarDate.From(Value);

    /// <summary>Whether a completion at that instant is late: its day in Germany lies after the due date.</summary>
    public bool IsMissedBy(Timestamp completedAt)
    {
        Ensure.That(completedAt).IsNotNull();
        return completedAt.DayInGermany() > AsDate();
    }

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
