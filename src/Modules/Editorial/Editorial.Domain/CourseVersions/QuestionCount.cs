namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>How many questions a test has, from 0 to 50.</summary>
public sealed record QuestionCount : IValueObject<int>
{
    private QuestionCount(int value) => Value = value;

    public int Value { get; }

    public static QuestionCount Default => new(10);

    public static QuestionCount From(int value)
    {
        Ensure.That(value).InRange(0, 50);
        return new QuestionCount(value);
    }
}
