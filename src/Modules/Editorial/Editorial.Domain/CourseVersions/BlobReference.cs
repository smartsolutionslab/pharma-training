using System.Diagnostics.CodeAnalysis;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>
/// Where a PDF or video lies in blob storage: the container-relative name, never a URL with a key, and never a nul or
/// other control character. <see cref="From"/> guards a value the code already trusts; <see cref="TryFrom"/> checks a
/// caller's, so a bad one is the caller's mistake rather than an exception.
/// </summary>
public sealed record BlobReference : IValueObject<string>
{
    public const int MaxLength = 1024;

    private BlobReference(string value) => Value = value;

    public string Value { get; }

    public static BlobReference From(string value)
    {
        Ensure.That(value)
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaxLength)
            .Satisfies(v => !v.Contains("://", StringComparison.Ordinal), "Eine Blob-Referenz ist ein Name, keine URL.")
            .Satisfies(v => !v.Any(char.IsControl), "Eine Blob-Referenz enthält keine Steuerzeichen.");
        return new BlobReference(value);
    }

    /// <summary>Succeeds exactly when <see cref="From"/> would, keeping the name as written.</summary>
    public static bool TryFrom(string? value, [NotNullWhen(true)] out BlobReference? result)
    {
        result = IsReference(value) ? new BlobReference(value) : null;
        return result is not null;
    }

    private static bool IsReference([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaxLength
            && !value.Contains("://", StringComparison.Ordinal)
            && !value.Any(char.IsControl);
}
