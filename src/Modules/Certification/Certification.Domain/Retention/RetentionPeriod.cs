using System.Diagnostics.CodeAnalysis;

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

    // stub for #391
    public static RetentionPeriod From(int value) => throw new NotImplementedException();

    // stub for #391
    public static bool TryFrom(int value, [NotNullWhen(true)] out RetentionPeriod? period) => throw new NotImplementedException();
}
