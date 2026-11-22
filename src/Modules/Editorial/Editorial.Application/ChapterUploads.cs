using System.Diagnostics;
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

    /// <summary>The header every PDF file starts with.</summary>
    private static ReadOnlySpan<byte> PdfHeader => "%PDF-"u8;

    /// <summary>The type of the box an MP4 file starts with, at bytes 4 to 7.</summary>
    private static ReadOnlySpan<byte> FileTypeBox => "ftyp"u8;

    /// <summary>How many leading bytes tell a PDF or an MP4: the longer of the two signatures, read from the start.</summary>
    private static int SignatureLength => Math.Max(PdfHeader.Length, BoxTypeOffset + FileTypeBox.Length);

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
    /// was put directly, not created by a server-side copy, which an upload URL also allows, and whose own leading bytes
    /// are the kind's: a PDF starts with <c>%PDF-</c>, an MP4 has <c>ftyp</c> at bytes 4 to 7. The declared content type
    /// is the uploader's word; the bytes are the file's.
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

        var start = await blobs.ReadStartAsync(BlobName.From(file.Value), SignatureLength, cancellationToken);
        if (!StartsAsTheKindDoes(start.Span, kind))
        {
            return Failure(CourseVersionFailures.UploadNotAccepted());
        }

        return Success(Unit.Value);
    }

    private static bool IsWhatTheKindTakes(StoredBlob blob, ChapterKind kind)
    {
        var mediaType = blob.ContentType.Split(';', 2)[0].Trim();
        return blob.IsBlockBlob
            && !blob.IsCopy
            && blob.Length > 0
            && blob.Length <= MaxBytesOf(kind)
            && string.Equals(mediaType, ContentTypeOf(kind), StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsAsTheKindDoes(ReadOnlySpan<byte> start, ChapterKind kind) => kind switch
    {
        ChapterKind.Pdf => start.StartsWith(PdfHeader),
        ChapterKind.Video => start.Length >= BoxTypeOffset + FileTypeBox.Length && start.Slice(BoxTypeOffset, FileTypeBox.Length).SequenceEqual(FileTypeBox),
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };

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
