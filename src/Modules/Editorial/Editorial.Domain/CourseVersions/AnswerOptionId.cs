namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>An answer option's identity within its question. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record AnswerOptionId : IValueObject<Guid>
{
    private AnswerOptionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AnswerOptionId New() => new(Guid.CreateVersion7());

    public static AnswerOptionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new AnswerOptionId(value);
    }

    public override string ToString() => Value.ToString();
}
