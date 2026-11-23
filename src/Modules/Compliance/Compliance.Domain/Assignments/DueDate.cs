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

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
