using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A PDF chapter carries what was extracted from its pages when it was recorded (#157): an outcome, and the text
/// exactly when the outcome has one. The next version copies both with the chapter; a chapter that becomes text or a
/// video drops them.
/// </summary>
public class PdfChapterTextTests
{
    private static readonly PdfText Extracted = PdfText.From("Zytostatika werden getrennt von anderen Arzneimitteln gelagert.");

    [Theory]
    [InlineData(PdfTextExtraction.Complete)]
    [InlineData(PdfTextExtraction.Truncated)]
    public void A_pdf_chapter_with_text_keeps_its_extraction_and_its_text(PdfTextExtraction extraction)
    {
        var chapter = Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Merkblatt"), BlobReference.From("course-versions/a/merkblatt.pdf"), extraction, Extracted);

        chapter.Kind.ShouldBe(ChapterKind.Pdf);
        chapter.Extraction.ShouldBe(extraction);
        chapter.ExtractedText.ShouldBe(Extracted);
        chapter.Text.ShouldBeNull("a PDF's extracted text is no Markdown an editor wrote");
    }

    [Fact]
    public void A_pdf_chapter_without_text_keeps_no_text()
    {
        var chapter = Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Scan"), BlobReference.From("course-versions/a/scan.pdf"), PdfTextExtraction.NoText, null);

        chapter.Extraction.ShouldBe(PdfTextExtraction.NoText);
        chapter.ExtractedText.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_chapter_without_text_given_a_text_is_a_bug()
    {
        Should.Throw<GuardException>(() =>
            Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Scan"), BlobReference.From("course-versions/a/scan.pdf"), PdfTextExtraction.NoText, Extracted));
    }

    [Theory]
    [InlineData(PdfTextExtraction.Complete)]
    [InlineData(PdfTextExtraction.Truncated)]
    public void A_pdf_chapter_with_text_given_none_is_a_bug(PdfTextExtraction extraction)
    {
        Should.Throw<GuardException>(() =>
            Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Merkblatt"), BlobReference.From("course-versions/a/merkblatt.pdf"), extraction, null));
    }

    [Fact]
    public void Text_and_video_chapters_have_no_extraction()
    {
        var text = TextChapter(ChapterId.New());
        var video = Chapter.OfVideo(ChapterId.New(), ChapterTitle.From("Vorführung"), BlobReference.From("course-versions/a/demo.mp4"), VideoDuration.From(90));

        text.Extraction.ShouldBeNull();
        text.ExtractedText.ShouldBeNull();
        video.Extraction.ShouldBeNull();
        video.ExtractedText.ShouldBeNull();
    }

    [Theory]
    [InlineData(PdfTextExtraction.Complete)]
    [InlineData(PdfTextExtraction.Truncated)]
    [InlineData(PdfTextExtraction.NoText)]
    public void The_next_version_copies_a_pdf_chapters_extraction_and_text(PdfTextExtraction extraction)
    {
        var text = extraction == PdfTextExtraction.NoText ? null : Extracted;
        var published = PublishedWithPdf(extraction, text);

        var next = published.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;

        var copied = next.Chapters.Single(chapter => chapter.Kind == ChapterKind.Pdf);
        copied.Extraction.ShouldBe(extraction);
        copied.ExtractedText.ShouldBe(text);
        copied.File.ShouldBe(published.Chapters.Single(chapter => chapter.Kind == ChapterKind.Pdf).File);
    }

    [Fact]
    public void A_pdf_chapter_replaced_by_a_text_chapter_drops_its_extraction_and_text()
    {
        var version = Draft();
        var chapter = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(chapter, ChapterTitle.From("Merkblatt"), BlobReference.From("course-versions/a/merkblatt.pdf"), PdfTextExtraction.Complete, Extracted)).IsSuccess.ShouldBeTrue();

        version.ReplaceChapter(TextChapter(chapter, "Merkblatt", "Jetzt als Text.")).IsSuccess.ShouldBeTrue();

        var replaced = version.Chapters.ShouldHaveSingleItem();
        replaced.Kind.ShouldBe(ChapterKind.Text);
        replaced.Extraction.ShouldBeNull();
        replaced.ExtractedText.ShouldBeNull();
        replaced.File.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_chapter_replaced_by_a_video_drops_its_extraction_and_text()
    {
        var version = Draft();
        var chapter = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(chapter, ChapterTitle.From("Merkblatt"), BlobReference.From("course-versions/a/merkblatt.pdf"), PdfTextExtraction.Truncated, Extracted)).IsSuccess.ShouldBeTrue();

        version.ReplaceChapter(Chapter.OfVideo(chapter, ChapterTitle.From("Vorführung"), BlobReference.From("course-versions/a/demo.mp4"), VideoDuration.From(90))).IsSuccess.ShouldBeTrue();

        var replaced = version.Chapters.ShouldHaveSingleItem();
        replaced.Extraction.ShouldBeNull();
        replaced.ExtractedText.ShouldBeNull();
    }

    [Fact]
    public void A_pdf_chapter_replaced_by_another_pdf_takes_the_new_ones_extraction()
    {
        var version = Draft();
        var chapter = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(chapter, ChapterTitle.From("Merkblatt"), BlobReference.From("course-versions/a/merkblatt.pdf"), PdfTextExtraction.Complete, Extracted)).IsSuccess.ShouldBeTrue();

        version.ReplaceChapter(Chapter.OfPdf(chapter, ChapterTitle.From("Scan"), BlobReference.From("course-versions/a/scan.pdf"), PdfTextExtraction.NoText, null)).IsSuccess.ShouldBeTrue();

        var replaced = version.Chapters.ShouldHaveSingleItem();
        replaced.Extraction.ShouldBe(PdfTextExtraction.NoText);
        replaced.ExtractedText.ShouldBeNull();
    }

    private static CourseVersion PublishedWithPdf(PdfTextExtraction extraction, PdfText? text)
    {
        var version = DraftWithChapter(out var chapter);
        version.AddChapter(Chapter.OfPdf(ChapterId.New(), ChapterTitle.From("Merkblatt"), BlobReference.From("course-versions/a/merkblatt.pdf"), extraction, text)).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        return version;
    }
}
