namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// A member's display name, as Organisation sent it. Kept as it was sent, untrimmed and without a length of Compliance's own; never blank and never holding a NUL
/// character, which PostgreSQL refuses.
/// </summary>
public sealed record MemberName : IValueObject<string>
{
    private MemberName(string value) => Value = value;

    public string Value { get; }

    public static MemberName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "must not contain a NUL character.");
        return new MemberName(value);
    }

    public override string ToString() => Value;
}
