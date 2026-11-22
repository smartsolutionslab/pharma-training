namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

/// <summary>
/// A member's display name, as Organisation sent it. Kept as it was sent, untrimmed; never blank, never longer than
/// Organisation allows a display name to be, and never holding a NUL character, which PostgreSQL refuses.
/// </summary>
public sealed record MemberName : IValueObject<string>
{
    /// <summary>Organisation's own maximum for a display name, which it trims before it sends one.</summary>
    public const int MaxLength = 100;

    private MemberName(string value) => Value = value;

    public string Value { get; }

    public static MemberName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(text => !text.Contains('\0', StringComparison.Ordinal), "must not contain a NUL character.");
        return new MemberName(value);
    }

    public override string ToString() => Value;
}
