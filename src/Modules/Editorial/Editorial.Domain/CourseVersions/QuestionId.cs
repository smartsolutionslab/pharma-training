namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>A test question's identity within its course version. A new one is a time-ordered Guid v7; an empty Guid is never one.</summary>
public sealed record QuestionId : IValueObject<Guid>
{
    private QuestionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static QuestionId New() => new(Guid.CreateVersion7());

    public static QuestionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new QuestionId(value);
    }

    public override string ToString() => Value.ToString();
}
