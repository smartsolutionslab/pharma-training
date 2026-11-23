namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>The holder's name as it stood when the certificate was issued; it never changes afterwards.</summary>
public sealed record HolderName : IValueObject<string>
{
    /// <summary>Organisation's own maximum for a display name.</summary>
    public const int MaxLength = 100;

    private HolderName(string value) => Value = value;

    public string Value { get; }

    public static HolderName From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new HolderName(value.Trim());
    }

    public override string ToString() => Value;
}
