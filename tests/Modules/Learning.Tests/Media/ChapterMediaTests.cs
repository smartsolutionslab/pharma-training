using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Media;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Media;

/// <summary>
/// Where Learning keeps its copy of a chapter's file in its own container, and how it is read (#41): the name is
/// derived from the version and the chapter, never generated, so a second copy of the same file finds the first.
/// </summary>
public class ChapterMediaTests
{
    private static readonly CourseVersionId Version = CourseVersionId.From(Guid.Parse("0193a1b2-1111-7000-8000-00000000000a"));

    private static readonly ChapterId Chapter = ChapterId.From(Guid.Parse("0193a1b2-2222-7000-8000-00000000000b"));

    [Fact]
    public void A_pdf_chapter_is_named_after_its_version_and_chapter_with_pdf()
    {
        ChapterMedia.NameFor(Version, Chapter, ChapterKind.Pdf)
            .ShouldBe(BlobName.From("course-versions/0193a1b2-1111-7000-8000-00000000000a/chapters/0193a1b2-2222-7000-8000-00000000000b.pdf"));
    }

    [Fact]
    public void A_video_chapter_is_named_with_mp4()
    {
        ChapterMedia.NameFor(Version, Chapter, ChapterKind.Video)
            .ShouldBe(BlobName.From("course-versions/0193a1b2-1111-7000-8000-00000000000a/chapters/0193a1b2-2222-7000-8000-00000000000b.mp4"));
    }

    [Fact]
    public void A_text_chapter_has_no_media_name()
    {
        Should.Throw<GuardException>(() => ChapterMedia.NameFor(Version, Chapter, ChapterKind.Text));
        Should.Throw<GuardException>(() => ChapterMedia.ReadHeadersFor(ChapterKind.Text));
        Should.Throw<GuardException>(() => ChapterMedia.ContentTypeOf(ChapterKind.Text));
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, "application/pdf")]
    [InlineData(ChapterKind.Video, "video/mp4")]
    public void Pdf_and_video_read_inline_with_their_content_type(ChapterKind kind, string contentType)
    {
        ChapterMedia.ContentTypeOf(kind).ShouldBe(contentType);
        ChapterMedia.ReadHeadersFor(kind).ShouldBe(BlobReadHeaders.Inline(contentType));
        ChapterMedia.ReadHeadersFor(kind).Disposition.ShouldBe(BlobDisposition.Inline);
    }
}
