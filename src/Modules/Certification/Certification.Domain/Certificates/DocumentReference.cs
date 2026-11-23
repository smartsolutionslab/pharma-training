namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

/// <summary>Where the rendered PDF lies in blob storage.</summary>
public sealed record DocumentReference : IValueObject<string>
{
    public const int MaxLength = 1024;

    private DocumentReference(string value) => Value = value;

    public string Value { get; }

    public static DocumentReference From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength);
        return new DocumentReference(value);
    }
}
