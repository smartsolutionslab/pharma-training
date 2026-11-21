namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>Where a PDF or video lies in blob storage: the container-relative name, never a URL with a key.</summary>
public sealed record BlobReference : IValueObject<string>
{
    public const int MaxLength = 1024;

    private BlobReference(string value) => Value = value;

    public string Value { get; }

    public static BlobReference From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).Satisfies(v => !v.Contains("://", StringComparison.Ordinal), "Eine Blob-Referenz ist ein Name, keine URL.");
        return new BlobReference(value);
    }
}
