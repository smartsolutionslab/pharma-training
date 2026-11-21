namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The name of a blob in a module's own container, folders separated by <c>/</c>: 1 to <see cref="MaxLength"/>
/// characters, relative, with no empty, <c>.</c> or <c>..</c> segment and no backslash or control character, so a signed
/// URL names exactly the blob the module meant and never one outside the path it chose.
/// </summary>
public sealed record BlobName : IValueObject<string>
{
    public const int MaxLength = 1024;

    private BlobName(string value) => Value = value;

    public string Value { get; }

    public static BlobName From(string value)
    {
        Ensure.That(value)
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaxLength)
            .Satisfies(name => !name.Any(character => character == '\\' || char.IsControl(character)), "must not contain a backslash or a control character.")
            .Satisfies(name => name.Split('/').All(IsSegment), "must be a relative path whose segments are neither empty, '.' nor '..'.");
        return new BlobName(value);
    }

    public override string ToString() => Value;

    private static bool IsSegment(string segment) => segment.Length > 0 && segment != "." && segment != "..";
}
