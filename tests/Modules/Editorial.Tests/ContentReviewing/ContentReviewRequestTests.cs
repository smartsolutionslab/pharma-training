using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing.ContentReviewVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// What the AI content review sends, worked out from the version before the model is called: its text chapters and the
/// stored text of its PDF chapters, numbered together from one in the version's order, whatever state the version is in;
/// videos and PDFs without text are left out and named (#302). A version without a chapter with text, or whose titles and
/// text chapters are more than <see cref="ContentReviewRequest.MaxCharacters"/>, is refused; PDFs share what is left.
/// </summary>
public class ContentReviewRequestTests
{
    public static TheoryData<CourseVersionStatus> Statuses
    {
        get
        {
            var data = new TheoryData<CourseVersionStatus>();
            foreach (var status in Enum.GetValues<CourseVersionStatus>())
            {
                data.Add(status);
            }

            return data;
        }
    }

    [Fact]
    public void The_text_chapters_are_numbered_from_one_in_the_versions_order()
    {
        var version = Draft();
        var first = ChapterId.New();
        var second = ChapterId.New();
        var third = ChapterId.New();
        version.AddChapter(TextChapter(first, "Händehygiene", "Hände vor und nach der Rezeptur desinfizieren.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(second, "Flächen", "Rezepturtisch und Waage täglich desinfizieren.")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(third, "Handschuhe", "Handschuhe bei jeder Rezeptur wechseln.")).IsSuccess.ShouldBeTrue();
        version.ReorderChapters([third, first, second]).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Number).ShouldBe([1, 2, 3]);
        request.Chapters.Select(chapter => chapter.Id).ShouldBe([third, first, second]);
    }

    [Fact]
    public void Pdf_chapters_without_text_and_video_chapters_are_left_out_and_do_not_shift_the_numbers_of_later_chapters()
    {
        var version = Draft();
        var text = ChapterId.New();
        var otherText = ChapterId.New();
        version.AddChapter(PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(text, "Händehygiene")).IsSuccess.ShouldBeTrue();
        version.AddChapter(VideoChapter("Lehrvideo")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(otherText, "Flächen")).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Id).ShouldBe([text, otherText]);
        request.Chapters.Select(chapter => chapter.Number).ShouldBe([1, 2]);
        request.LeftOutChapters.Count.ShouldBe(2);
    }

    [Fact]
    public void Text_and_pdf_chapters_with_text_are_numbered_together_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(6, "TPVNTR", out var ids);

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Number).ShouldBe([1, 2, 3, 4]);
        request.Chapters.Select(chapter => chapter.Id).ShouldBe([ids[0], ids[1], ids[4], ids[5]]);
        request.Chapters.Select(chapter => chapter.Source).ShouldBe([ContentReviewSource.Text, ContentReviewSource.Pdf, ContentReviewSource.Text, ContentReviewSource.Pdf]);
        request.Chapters.Select(chapter => chapter.Text).ShouldBe([TestDraftVersions.TextOf('T', 1), TestDraftVersions.TextOf('P', 2), TestDraftVersions.TextOf('T', 5), TestDraftVersions.TextOf('R', 6)]);
        request.Chapters[1].Title.ShouldBe(ChapterTitle.From("PDF-Kapitel 2"));
        request.LeftOutChapters.ShouldBe([ids[2], ids[3]]);
        request.TruncatedChapters.ShouldBe([ids[5]]);
    }

    [Fact]
    public void A_version_of_pdf_chapters_with_text_only_is_reviewed()
    {
        var version = TestDraftVersions.WithChapters(5, "PP", out var ids);

        var result = ContentReviewRequest.For(version);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.Chapters.Select(chapter => chapter.Id).ShouldBe(ids);
        result.Value.Chapters.ShouldAllBe(chapter => chapter.Source == ContentReviewSource.Pdf);
        result.Value.LeftOutChapters.ShouldBeEmpty();
        result.Value.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void Videos_pdfs_without_text_and_pdfs_recorded_before_extraction_are_left_out_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(3, "LTVNPL", out var ids);

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Id).ShouldBe([ids[1], ids[4]]);
        request.LeftOutChapters.ShouldBe([ids[0], ids[2], ids[3], ids[5]]);
        request.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void Pdf_chapters_whose_extraction_was_cut_are_reviewed_and_named_as_truncated_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(4, "RTPR", out var ids);

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Id).ShouldBe(ids);
        request.TruncatedChapters.ShouldBe([ids[0], ids[3]]);
        request.LeftOutChapters.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("NV")]
    [InlineData("L")]
    [InlineData("NLV")]
    public void A_version_whose_pdfs_have_no_text_is_refused_with_no_text_chapters_to_review(string layout)
    {
        var version = TestDraftVersions.WithChapters(3, layout, out _);

        var result = ContentReviewRequest.For(version);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.no-text-chapters-to-review");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_pdf_whose_text_would_pass_the_limit_is_cut_to_what_is_left_and_named_as_truncated()
    {
        // A text chapter "A" of the longest Markdown and a PDF chapter "B" whose text is one character longer than what
        // the limit leaves it: the titles and the text chapter are sent whole, and the PDF gives way.
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        var left = ContentReviewRequest.MaxCharacters - MarkdownText.MaxLength - 2;
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "B", new string('b', left + 1))).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Single(chapter => chapter.Id == pdf).Text.Length.ShouldBe(left);
        Characters(request).ShouldBe(ContentReviewRequest.MaxCharacters);
        request.TruncatedChapters.ShouldBe([pdf]);
        request.LeftOutChapters.ShouldBeEmpty();
    }

    [Fact]
    public void A_pdf_that_fits_what_is_left_is_sent_whole_and_not_named()
    {
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        var left = ContentReviewRequest.MaxCharacters - MarkdownText.MaxLength - 2;
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "B", new string('b', left))).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Single(chapter => chapter.Id == pdf).Text.Length.ShouldBe(left);
        request.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void Long_pdfs_share_the_limit_a_short_one_is_sent_whole_and_the_version_is_reviewed()
    {
        // The issue's case: a version made of PDFs is reviewed on all of them, never refused for their length.
        var version = Draft();
        var longFirst = ChapterId.New();
        var shortOne = ChapterId.New();
        var longSecond = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(longFirst, "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TestDraftVersions.PdfWithText(shortOne, "B", new string('b', 1_000))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TestDraftVersions.PdfWithText(longSecond, "C", new string('c', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        var lengths = request.Chapters.Select(chapter => chapter.Text.Length).ToList();
        lengths[1].ShouldBe(1_000);
        Math.Abs(lengths[0] - lengths[2]).ShouldBeLessThanOrEqualTo(1, "the long PDFs share what is left evenly");
        Characters(request).ShouldBe(ContentReviewRequest.MaxCharacters);
        request.TruncatedChapters.ShouldBe([longFirst, longSecond]);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', 100))).IsSuccess.ShouldBeTrue();
        var left = ContentReviewRequest.MaxCharacters - MarkdownText.MaxLength - 100 - 3;
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "C", new string('c', left - 1) + "\U0001F600" + "cc")).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.Single(chapter => chapter.Id == pdf).Text.ShouldBe(new string('c', left - 1));
        request.TruncatedChapters.ShouldBe([pdf]);
    }

    [Fact]
    public void A_pdf_the_text_chapters_leave_no_room_for_is_left_out_and_named()
    {
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', ContentReviewRequest.MaxCharacters - MarkdownText.MaxLength - 3))).IsSuccess.ShouldBeTrue();
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "C", new string('c', 1_000))).IsSuccess.ShouldBeTrue();

        var request = ContentReviewRequest.For(version).Value;

        request.Chapters.ShouldNotContain(chapter => chapter.Id == pdf);
        request.LeftOutChapters.ShouldBe([pdf]);
        request.TruncatedChapters.ShouldBeEmpty();
        Characters(request).ShouldBeLessThanOrEqualTo(ContentReviewRequest.MaxCharacters);
    }

    [Fact]
    public void Each_chapter_keeps_its_id_title_and_text()
    {
        var version = Draft();
        var id = ChapterId.New();
        version.AddChapter(TextChapter(id, "Händehygiene", "Hände vor und nach der Rezeptur desinfizieren.")).IsSuccess.ShouldBeTrue();

        var chapter = ContentReviewRequest.For(version).Value.Chapters.ShouldHaveSingleItem();

        chapter.Number.ShouldBe(1);
        chapter.Id.ShouldBe(id);
        chapter.Title.ShouldBe(ChapterTitle.From("Händehygiene"));
        chapter.Source.ShouldBe(ContentReviewSource.Text);
        chapter.Text.ShouldBe("Hände vor und nach der Rezeptur desinfizieren.");
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Every_status_can_be_reviewed(CourseVersionStatus status)
    {
        var version = In(status);
        version.Status.ShouldBe(status);

        var result = ContentReviewRequest.For(version);

        result.IsSuccess.ShouldBeTrue(status.ToString());
        result.Value.Chapters.Select(chapter => chapter.Id).ShouldBe(TextChapterIds(version));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_version_without_a_chapter_with_text_is_refused_with_no_text_chapters_to_review(bool withFileChapters)
    {
        var version = Draft();
        if (withFileChapters)
        {
            version.AddChapter(PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
            version.AddChapter(VideoChapter("Lehrvideo")).IsSuccess.ShouldBeTrue();
        }

        var result = ContentReviewRequest.For(version);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.no-text-chapters-to-review");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Content_above_the_limit_is_refused_and_content_at_the_limit_is_not(int beyondTheLimit, bool refused)
    {
        // Two chapters' titles and texts add up to the limit, plus what goes beyond it. A PDF chapter's title is not
        // sent, so it does not count.
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(PdfChapter("Ein Merkblatt mit einem langen Titel")).IsSuccess.ShouldBeTrue();
        var rest = ContentReviewRequest.MaxCharacters - MarkdownText.MaxLength - 2 + beyondTheLimit;
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', rest))).IsSuccess.ShouldBeTrue();

        var result = ContentReviewRequest.For(version);

        result.IsFailure.ShouldBe(refused);
        if (refused)
        {
            result.Error.Code.ShouldBe("editorial.too-much-content-to-review");
            result.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
        else
        {
            result.Value.Chapters.Count.ShouldBe(2);
        }
    }

    private static long Characters(ContentReviewRequest request) =>
        request.Chapters.Sum(chapter => (long)chapter.Title.Value.Length + chapter.Text.Length);
}
