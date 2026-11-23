namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>An answer option of a published test question. An empty Guid is never one.</summary>
public sealed record AnswerOptionId : IValueObject<Guid>
{
    private AnswerOptionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AnswerOptionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new AnswerOptionId(value);
    }

    public override string ToString() => Value.ToString();
}
