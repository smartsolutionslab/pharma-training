namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

/// <summary>A question of a published test definition; Editorial's question id belongs to one version. An empty Guid is never one.</summary>
public sealed record QuestionVersionId : IValueObject<Guid>
{
    private QuestionVersionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static QuestionVersionId From(Guid value)
    {
        Ensure.That(value).IsNotEmpty();
        return new QuestionVersionId(value);
    }

    public override string ToString() => Value.ToString();
}
