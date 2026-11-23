namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>A number of questions, never negative.</summary>
public sealed record QuestionCount : IValueObject<int>
{
    private QuestionCount(int value) => Value = value;

    public int Value { get; }

    public static QuestionCount From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new QuestionCount(value);
    }
}
