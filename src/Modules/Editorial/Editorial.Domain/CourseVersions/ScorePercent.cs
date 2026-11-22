using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A share of the test's points, from 0 to 100 percent.</summary>
public sealed record ScorePercent : IValueObject<int>
{
    public const int Minimum = 0;
    public const int Maximum = 100;

    private ScorePercent(int value) => Value = value;

    public int Value { get; }

    public static ScorePercent From(int value)
    {
        Ensure.That(value).InRange(Minimum, Maximum);
        return new ScorePercent(value);
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would.</summary>
    public static bool TryFrom(int value, [NotNullWhen(true)] out ScorePercent? result)
    {
        result = value is >= Minimum and <= Maximum ? new ScorePercent(value) : null;
        return result is not null;
    }
}
