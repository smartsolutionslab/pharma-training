using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A share of the test's points, from 0 to 100 percent.</summary>
public sealed record ScorePercent : IValueObject<int>
{
    // Red-first stub for #17: the values and TryFrom are wrong on purpose until the change lands.
    public const int Minimum = -1;
    public const int Maximum = -1;

    private ScorePercent(int value) => Value = value;

    public int Value { get; }

    public static ScorePercent From(int value)
    {
        Ensure.That(value).InRange(0, 100);
        return new ScorePercent(value);
    }

    public static bool TryFrom(int value, [NotNullWhen(true)] out ScorePercent? result)
    {
        result = new ScorePercent(-1);
        return true;
    }
}
