using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

/// <summary>How many whole years a pharmacy keeps a certificate in its view after its issue: 2 to 30, 5 by default (#391).</summary>
public sealed record RetentionPeriod : IValueObject<int>
{
    public const int MinimumYears = 2;
    public const int MaximumYears = 30;
    public const int DefaultYears = 5;

    private RetentionPeriod(int value) => Value = value;

    public static RetentionPeriod Default => new(DefaultYears);

    public int Value { get; }

    public static RetentionPeriod From(int value)
    {
        Ensure.That(value).InRange(MinimumYears, MaximumYears);
        return new RetentionPeriod(value);
    }

    /// <summary>The period of so many years, or false where it lies outside two to thirty: for a caller's input.</summary>
    public static bool TryFrom(int value, [NotNullWhen(true)] out RetentionPeriod? period)
    {
        period = value is >= MinimumYears and <= MaximumYears ? new RetentionPeriod(value) : null;
        return period is not null;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
