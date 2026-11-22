using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>How many questions a test has, from 0 to 50.</summary>
public sealed record QuestionCount : IValueObject<int>
{
    public const int Minimum = 0;
    public const int Maximum = 50;

    private QuestionCount(int value) => Value = value;

    public int Value { get; }

    public static QuestionCount Default => new(10);

    public static QuestionCount From(int value)
    {
        Ensure.That(value).InRange(Minimum, Maximum);
        return new QuestionCount(value);
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would.</summary>
    public static bool TryFrom(int value, [NotNullWhen(true)] out QuestionCount? result)
    {
        result = value is >= Minimum and <= Maximum ? new QuestionCount(value) : null;
        return result is not null;
    }
}
