namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A share of the test's points, from 0 to 100 percent.</summary>
public sealed record ScorePercent : IValueObject<int>
{
    private ScorePercent(int value) => Value = value;

    public int Value { get; }

    public static ScorePercent From(int value)
    {
        Ensure.That(value).InRange(0, 100);
        return new ScorePercent(value);
    }
}
