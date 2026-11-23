namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>
/// The code in the QR link. Random and long, created by the Application from a cryptographic source, and never the
/// certificate's id: knowing one certificate must not let anyone guess another.
/// </summary>
public sealed record VerificationCode : IValueObject<string>
{
    public const int MinLength = 24;
    public const int MaxLength = 64;

    private VerificationCode(string value) => Value = value;

    public string Value { get; }

    public static VerificationCode From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMinLength(MinLength).HasMaxLength(MaxLength);
        return new VerificationCode(value);
    }
}
