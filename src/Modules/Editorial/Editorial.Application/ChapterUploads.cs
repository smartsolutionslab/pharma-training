using System.Diagnostics;
using System.Text;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The uploads of chapters' PDFs and videos (docs/adr/0002): the module names each one
/// <c>course-versions/{version}/{guid v7}.pdf|.mp4</c>, so a name is unguessable and holds nothing a user wrote, and a
/// chapter takes a file only when its name was issued for the chapter's version and kind and the blob uploaded under it
/// is what the kind asks for, since a signed upload URL limits neither size nor content type.
/// </summary>
public static class ChapterUploads
{
    /// <summary>The largest PDF an editor may upload, 50 MiB.</summary>
    public const long MaxPdfBytes = 52_428_800;

    /// <summary>The largest video an editor may upload, 2 GiB: one browser upload within the URL's fifteen minutes.</summary>
    public const long MaxVideoBytes = 2_147_483_648;

    private const string Prefix = "course-versions";

    private const string PdfContentType = "application/pdf";

    private const string VideoContentType = "video/mp4";

    /// <summary>The length of a Guid in its "D" format, <c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c>.</summary>
    private const int GuidLength = 36;

    /// <summary>Where an MP4's first box names its type, <c>ftyp</c>: after the box's four-byte size.</summary>
    private const int BoxTypeOffset = 4;

    /// <summary>Where an MP4's ftyp box names its major brand: after the box's size and type.</summary>
    private const int MajorBrandOffset = 8;

    /// <summary>The length of a brand, four ASCII characters.</summary>
    private const int BrandLength = 4;

    /// <summary>
    /// The major brands of an MP4 file, matched exactly (ordinal, the trailing space included): the ISO base media brands
    /// an MP4 writer names (ISO/IEC 14496-12, <c>isom</c> and <c>iso2</c> to <c>iso9</c>), MP4 itself (ISO/IEC 14496-14,
    /// <c>mp41</c>, <c>mp42</c>), AVC in MP4 (<c>avc1</c>), DASH segments (<c>dash</c>), CMAF (<c>cmfc</c>, <c>cmf2</c>),
    /// Apple's MP4 video (<c>M4V </c>), Sony's XAVC and MP4 video (<c>XAVC</c>, <c>MSNV</c>) and the mobile MP4 brand
    /// <c>mmp4</c>. A QuickTime movie, a HEIF or AVIF image, a 3GP file or a Flash video (<c>f4v </c>) also starts with an
    /// ftyp box, but names a brand of its own and is refused.
    /// </summary>
    private static readonly string[] Mp4MajorBrands =
    [
        "isom", "iso2", "iso3", "iso4", "iso5", "iso6", "iso7", "iso8", "iso9", "mp41", "mp42", "avc1", "dash", "cmfc", "cmf2", "M4V ", "XAVC", "MSNV", "mmp4",
    ];

    /// <summary>The header every PDF file starts with.</summary>
    private static ReadOnlySpan<byte> PdfHeader => "%PDF-"u8;

    /// <summary>The type of the box an MP4 file starts with, at bytes 4 to 7.</summary>
    private static ReadOnlySpan<byte> FileTypeBox => "ftyp"u8;

    /// <summary>How many leading bytes tell a PDF or an MP4: the longer of the two signatures, read from the start.</summary>
    private static int SignatureLength => Math.Max(PdfHeader.Length, MajorBrandOffset + BrandLength);

    /// <summary>A new name for one upload of a PDF or video for the version.</summary>
    public static BlobName NameFor(CourseVersionId version, ChapterKind kind)
    {
        Ensure.That(version).IsNotNull();
        return BlobName.From($"{PrefixOf(version)}{Guid.CreateVersion7():D}{ExtensionOf(TakingAFile(kind))}");
    }

    /// <summary>Whether the file is a name <see cref="NameFor"/> issues for the version and kind, exactly.</summary>
    public static bool IsIssuedFor(BlobReference file, CourseVersionId version, ChapterKind kind)
    {
        Ensure.That(file).IsNotNull();
        Ensure.That(version).IsNotNull();

        if (kind is not (ChapterKind.Pdf or ChapterKind.Video))
        {
            return false;
        }

        var prefix = PrefixOf(version);
        var extension = ExtensionOf(kind);
        var name = file.Value;
        return name.Length == prefix.Length + GuidLength + extension.Length
            && name.StartsWith(prefix, StringComparison.Ordinal)
            && name.EndsWith(extension, StringComparison.Ordinal)
            && IsIssuedGuid(name.Substring(prefix.Length, GuidLength));
    }

    /// <summary>The content type the browser uploads a file of the kind with.</summary>
    public static string ContentTypeOf(ChapterKind kind) => TakingAFile(kind) switch
    {
        ChapterKind.Pdf => PdfContentType,
        ChapterKind.Video => VideoContentType,
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };

    /// <summary>The most bytes a file of the kind may have.</summary>
    public static long MaxBytesOf(ChapterKind kind) => TakingAFile(kind) switch
    {
        ChapterKind.Pdf => MaxPdfBytes,
        ChapterKind.Video => MaxVideoBytes,
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };

    /// <summary>
    /// Whether the file may become the content of a chapter of the version and kind: a name issued for them, under which
    /// a block blob of the kind's content type (in any case, with any parameters), not empty and at most the kind's size,
    /// was put directly, not created by a server-side copy, which an upload URL also allows, with no content disposition
    /// or encoding of the uploader's, and whose own leading bytes
    /// are the kind's: a PDF starts with <c>%PDF-</c>, an MP4 has <c>ftyp</c> at bytes 4 to 7 and an MP4 major brand at
    /// bytes 8 to 11. The declared content type is the uploader's word; the bytes are the file's.
    /// </summary>
    public static async Task<Result<Unit, ApiError>> AcceptAsync(IStoredBlobs blobs, BlobReference file, CourseVersionId version, ChapterKind kind, CancellationToken cancellationToken)
    {
        Ensure.That(blobs).IsNotNull();

        if (!IsIssuedFor(file, version, kind))
        {
            return Failure(CourseVersionFailures.InvalidChapterFile());
        }

        // An issued name is a blob name: the module wrote it.
        var stored = await blobs.FindAsync(BlobName.From(file.Value), cancellationToken);
        if (!stored.HasValue)
        {
            return Failure(CourseVersionFailures.UploadNotFound());
        }

        if (!IsWhatTheKindTakes(stored.Value, kind))
        {
            return Failure(CourseVersionFailures.UploadNotAccepted());
        }

        // The read carries the found blob's ETag: a blob replaced since its lookup answers no bytes and is refused.
        var start = await blobs.ReadStartAsync(stored.Value, SignatureLength, cancellationToken);
        if (!StartsAsTheKindDoes(start.Span, kind))
        {
            return Failure(CourseVersionFailures.UploadNotAccepted());
        }

        return Success(Unit.Value);
    }

    private static bool IsWhatTheKindTakes(StoredBlob blob, ChapterKind kind)
    {
        var mediaType = blob.ContentType.Split(';', 2)[0].Trim();
        // A read URL serves the blob with the disposition and encoding its uploader set: the module sets those, not the uploader.
        return blob.IsBlockBlob
            && !blob.IsCopy
            && blob.ContentDisposition is null
            && blob.ContentEncoding is null
            && blob.Length > 0
            && blob.Length <= MaxBytesOf(kind)
            && string.Equals(mediaType, ContentTypeOf(kind), StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsAsTheKindDoes(ReadOnlySpan<byte> start, ChapterKind kind) => kind switch
    {
        ChapterKind.Pdf => start.StartsWith(PdfHeader),
        ChapterKind.Video => start.Length >= MajorBrandOffset + BrandLength
            && start.Slice(BoxTypeOffset, FileTypeBox.Length).SequenceEqual(FileTypeBox)
            && IsMp4MajorBrand(start.Slice(MajorBrandOffset, BrandLength)),
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };

    private static bool IsMp4MajorBrand(ReadOnlySpan<byte> brand) => Mp4MajorBrands.Contains(Encoding.ASCII.GetString(brand), StringComparer.Ordinal);

    /// <summary>The kind, when it takes a file; a text chapter's has none, and asking for it is a bug.</summary>
    private static ChapterKind TakingAFile(ChapterKind kind)
    {
        Ensure.That((int)kind, nameof(kind)).Satisfies(_ => kind is ChapterKind.Pdf or ChapterKind.Video, "Only a PDF or video chapter has a file.");
        return kind;
    }

    /// <summary>A Guid as <see cref="NameFor"/> writes it, in lower case: "D" parsing alone would take upper case too.</summary>
    private static bool IsIssuedGuid(string segment) =>
        Guid.TryParseExact(segment, "D", out var parsed) && string.Equals(segment, parsed.ToString("D"), StringComparison.Ordinal);

    private static string PrefixOf(CourseVersionId version) => $"{Prefix}/{version.Value:D}/";

    private static string ExtensionOf(ChapterKind kind) => kind switch
    {
        ChapterKind.Pdf => ".pdf",
        ChapterKind.Video => ".mp4",
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };
}
