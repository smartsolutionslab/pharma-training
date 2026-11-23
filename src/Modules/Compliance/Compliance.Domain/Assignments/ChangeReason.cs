namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;

/// <summary>Why a manager changed or cancelled an assignment; shown in the history.</summary>
public sealed record ChangeReason : IValueObject<string>
{
    public const int MaxLength = 500;

    private ChangeReason(string value) => Value = value;

    public static ChangeReason MembershipEnded => new("Mitgliedschaft beendet");

    public string Value { get; }

    public static ChangeReason From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new ChangeReason(value);
    }
}
