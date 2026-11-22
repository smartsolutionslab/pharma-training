using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing.ContentReviewVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// What the AI content review sends, worked out from the version before the model is called: its text chapters only,
/// numbered from one in the version's order, whatever state the version is in. A version without a text chapter, or
/// with more text than <see cref="ContentReviewRequest.MaxCharacters"/>, is refused.
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
    public void Pdf_and_video_chapters_are_left_out_and_do_not_shift_the_numbers_of_later_text_chapters()
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
        chapter.Text.ShouldBe(MarkdownText.From("Hände vor und nach der Rezeptur desinfizieren."));
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
    public void A_version_without_a_text_chapter_is_refused_with_no_text_chapters_to_review(bool withFileChapters)
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
}
