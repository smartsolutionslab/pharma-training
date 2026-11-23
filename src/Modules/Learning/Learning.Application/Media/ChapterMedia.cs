using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Media;

/// <summary>
/// Learning's copy of a chapter's PDF or video in its own container, <c>learning-blobs</c> (#41, docs/adr/0002): named
/// <c>course-versions/{version}/chapters/{chapter}.pdf|.mp4</c>, derived from the version and the chapter and never
/// generated, so a second copy of the same file finds the first; read inline with the kind's content type. A text
/// chapter has no file, and asking for one is a bug.
/// </summary>
public static class ChapterMedia
{
    private const string PdfContentType = "application/pdf";

    private const string VideoContentType = "video/mp4";

    /// <summary>Where Learning keeps its copy of the chapter's file in the version.</summary>
    public static BlobName NameFor(CourseVersionId version, ChapterId chapter, ChapterKind kind)
    {
        Ensure.That(version).IsNotNull();
        Ensure.That(chapter).IsNotNull();
        return BlobName.From($"course-versions/{version.Value:D}/chapters/{chapter.Value:D}{ExtensionOf(TakingAFile(kind))}");
    }

    /// <summary>The headers a read URL on the copy is signed with: the kind's content type, shown inline.</summary>
    public static BlobReadHeaders ReadHeadersFor(ChapterKind kind) => BlobReadHeaders.Inline(ContentTypeOf(kind));

    /// <summary>
    /// The content type a file of the kind is served with. It is the kind's, not the copied blob's property: Editorial
    /// accepts an upload only with the kind's type, and a wrong property never picks the type.
    /// </summary>
    public static string ContentTypeOf(ChapterKind kind) => TakingAFile(kind) switch
    {
        ChapterKind.Pdf => PdfContentType,
        ChapterKind.Video => VideoContentType,
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };

    private static string ExtensionOf(ChapterKind kind) => kind switch
    {
        ChapterKind.Pdf => ".pdf",
        ChapterKind.Video => ".mp4",
        _ => throw new UnreachableException($"A {kind} chapter has no file."),
    };

    /// <summary>The kind, when it takes a file; a text chapter's has none, and asking for it is a bug.</summary>
    private static ChapterKind TakingAFile(ChapterKind kind)
    {
        Ensure.That((int)kind, nameof(kind)).Satisfies(_ => kind is ChapterKind.Pdf or ChapterKind.Video, "Only a PDF or video chapter has a file.");
        return kind;
    }
}
