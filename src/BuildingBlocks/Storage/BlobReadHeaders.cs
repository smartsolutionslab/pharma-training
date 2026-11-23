using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// The response headers a read URL is signed with, <c>rsct</c> (Content-Type) and <c>rscd</c> (Content-Disposition), so
/// the storage service serves the blob with the type the module chose, never the one its uploader set (docs/adr/0002).
/// A type is one bare <c>type/subtype</c>, with no parameters, whitespace or line break. Inline is signed only for
/// <c>application/pdf</c> and <c>video/mp4</c>, the two types the player shows; any other type reads as an attachment.
/// </summary>
public sealed partial record BlobReadHeaders
{
    private static readonly string[] InlineTypes = ["application/pdf", "video/mp4"];

    private BlobReadHeaders(string contentType, BlobDisposition disposition)
    {
        ContentType = contentType;
        Disposition = disposition;
    }

    public string ContentType { get; }

    public BlobDisposition Disposition { get; }

    /// <summary>Shown in place: only a PDF or an MP4 video.</summary>
    public static BlobReadHeaders Inline(string contentType)
    {
        Guard(contentType)
            .Satisfies(type => InlineTypes.Contains(type, StringComparer.Ordinal), "only application/pdf and video/mp4 are served inline.");
        return new BlobReadHeaders(contentType, BlobDisposition.Inline);
    }

    /// <summary>Saved as a file: any well-formed type.</summary>
    public static BlobReadHeaders Attachment(string contentType)
    {
        Guard(contentType);
        return new BlobReadHeaders(contentType, BlobDisposition.Attachment);
    }

    private static EnsuredString Guard(string contentType) =>
        Ensure.That(contentType)
            .IsNotNullOrWhiteSpace()
            .Matches(MediaType(), "must be one bare type/subtype in lower case, without parameters, whitespace or a line break.");

    [GeneratedRegex("^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$")]
    private static partial Regex MediaType();
}
