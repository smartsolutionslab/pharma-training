using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>How often an assignment repeats, in months: 12 for the yearly briefing, 24 for first aid.</summary>
public sealed record RecurrenceInterval : IValueObject<int>
{
    public const int MinimumMonths = 1;
    public const int MaximumMonths = 60;

    private RecurrenceInterval(int value) => Value = value;

    public static RecurrenceInterval Yearly => new(12);

    public int Value { get; }

    public static RecurrenceInterval From(int value)
    {
        Ensure.That(value).InRange(MinimumMonths, MaximumMonths);
        return new RecurrenceInterval(value);
    }

    /// <summary>The interval of so many months, or false where it lies outside one to sixty: for a caller's input.</summary>
    public static bool TryFrom(int value, [NotNullWhen(true)] out RecurrenceInterval? interval)
    {
        interval = value is >= MinimumMonths and <= MaximumMonths ? new RecurrenceInterval(value) : null;
        return interval is not null;
    }
}
