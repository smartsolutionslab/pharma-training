using System.Diagnostics.CodeAnalysis;

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

    /// <summary>Red-test stub for #15 (task A2): refuses every value. B3 checks a caller's name as <see cref="From"/> does.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out BlobReference? result)
    {
        result = null;
        return false;
    }
}
