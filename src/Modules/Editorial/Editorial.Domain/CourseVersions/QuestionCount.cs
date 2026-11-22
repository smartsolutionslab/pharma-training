using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>How many questions a test has, from 0 to 50.</summary>
public sealed record QuestionCount : IValueObject<int>
{
    // Red-first stub for #17: the values and TryFrom are wrong on purpose until the change lands.
    public const int Minimum = -1;
    public const int Maximum = -1;

    private QuestionCount(int value) => Value = value;

    public int Value { get; }

    public static QuestionCount Default => new(10);

    public static QuestionCount From(int value)
    {
        Ensure.That(value).InRange(0, 50);
        return new QuestionCount(value);
    }

    public static bool TryFrom(int value, [NotNullWhen(true)] out QuestionCount? result)
    {
        result = new QuestionCount(-1);
        return true;
    }
}
