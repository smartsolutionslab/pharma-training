using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>How many months a certificate stays valid, and how often a recurring assignment repeats.</summary>
public sealed record RecurrenceInterval : IValueObject<int>
{
    public const int MinimumMonths = 1;
    public const int MaximumMonths = 60;

    private RecurrenceInterval(int value) => Value = value;

    public int Value { get; }

    public static RecurrenceInterval Yearly => new(12);

    public static RecurrenceInterval From(int value)
    {
        Ensure.That(value).InRange(MinimumMonths, MaximumMonths);
        return new RecurrenceInterval(value);
    }

    // Red-first stub for #17: wrong on purpose until the change lands.
    public static bool TryFrom(int value, [NotNullWhen(true)] out RecurrenceInterval? result)
    {
        result = new RecurrenceInterval(-1);
        return true;
    }
}
