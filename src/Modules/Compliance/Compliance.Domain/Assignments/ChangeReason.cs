using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>Why a manager changed or cancelled an assignment; shown in the history.</summary>
public sealed record ChangeReason : IValueObject<string>
{
    public const int MaxLength = 500;

    public const string PlainMessage = "holds no control or format characters but line breaks and tabs.";

    private ChangeReason(string value) => Value = value;

    public static ChangeReason MembershipEnded => new("Mitgliedschaft beendet");

    /// <summary>A manager withdrew the assignment; a withdrawal carries no reason of the manager's own.</summary>
    public static ChangeReason Withdrawn => new("Von der Leitung zurückgezogen");

    public string Value { get; }

    public static ChangeReason From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(IsPlain, PlainMessage);
        return new ChangeReason(value);
    }

    /// <summary>The reason when it is one: not blank, at most <see cref="MaxLength"/> characters, plain text.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out ChangeReason? result)
    {
        result = value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= MaxLength && IsPlain(value) ? new ChangeReason(value) : null;
        return result is not null;
    }

    // Kept in the assignment's jsonb history, where PostgreSQL refuses U+0000 (22P05): a reason holds no control or format
    // characters but line breaks and tabs, as Editorial's return reason.
    private static bool IsPlain(string value) => PlainText.IsPlain(value, allowLineBreaksAndTabs: true);
}
