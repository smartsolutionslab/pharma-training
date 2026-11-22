using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing.ContentReviewerAskTests;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>
/// The model's answer, checked as untrusted input and mapped onto the version's chapters: a hint on a chapter that was
/// not sent, a hint text that cannot be shown, or more hints on one chapter than asked for rejects the whole answer;
/// no hints and duplicate hints are accepted.
/// </summary>
public class ContentReviewAnswerTests
{
    [Fact]
    public void Each_hint_is_mapped_to_its_chapters_id_kind_and_trimmed_text()
    {
        var version = ContentReviewVersions.WithTextChapters("Händehygiene");
        version.AddChapter(ContentReviewVersions.PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfText(ChapterId.New(), ChapterTitle.From("Flächen"), MarkdownText.From("Rezepturtisch und Waage täglich."))).IsSuccess.ShouldBeTrue();
        var chapters = ContentReviewVersions.TextChapterIds(version);
        var request = ContentReviewRequest.For(version).Value;
        var answer = Answer(
            Hint(1, ContentReviewAnswerKind.Gap, "  Schritt 3 fehlt.\n"),
            Hint(2, ContentReviewAnswerKind.Contradiction, "\t„täglich“ widerspricht Kapitel „Händehygiene“. "),
            Hint(2, ContentReviewAnswerKind.Readability, "Der Satz ist zu lang."));

        var review = answer.ToReview(request).Value;

        review.Hints.ShouldBe(
        [
            new ContentHint(chapters[0], ContentHintKind.Gap, "Schritt 3 fehlt."),
            new ContentHint(chapters[1], ContentHintKind.Contradiction, "„täglich“ widerspricht Kapitel „Händehygiene“."),
            new ContentHint(chapters[1], ContentHintKind.Readability, "Der Satz ist zu lang."),
        ]);
    }

    [Fact]
    public void Hints_are_ordered_by_chapter_and_keep_the_models_order_within_one()
    {
        var version = ContentReviewVersions.WithTextChapters("Händehygiene", "Flächen", "Handschuhe");
        var chapters = ContentReviewVersions.TextChapterIds(version);
        var request = ContentReviewRequest.For(version).Value;
        var answer = Answer(
            Hint(3, ContentReviewAnswerKind.Gap, "c1"),
            Hint(1, ContentReviewAnswerKind.Readability, "a1"),
            Hint(3, ContentReviewAnswerKind.Contradiction, "c2"),
            Hint(1, ContentReviewAnswerKind.Gap, "a2"),
            Hint(2, ContentReviewAnswerKind.Gap, "b1"));

        var review = answer.ToReview(request).Value;

        review.Hints.Select(hint => hint.Text).ShouldBe(["a1", "a2", "b1", "c1", "c2"]);
        review.Hints.Select(hint => hint.Chapter).ShouldBe([chapters[0], chapters[0], chapters[1], chapters[2], chapters[2]]);
    }

    [Fact]
    public void The_review_lists_the_reviewed_chapters_and_not_pdf_or_video_ones()
    {
        var version = ContentReviewVersions.WithTextChapters("Händehygiene");
        version.AddChapter(ContentReviewVersions.PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfText(ChapterId.New(), ChapterTitle.From("Flächen"), MarkdownText.From("Rezepturtisch und Waage täglich."))).IsSuccess.ShouldBeTrue();
        version.AddChapter(ContentReviewVersions.VideoChapter("Lehrvideo")).IsSuccess.ShouldBeTrue();
        var request = ContentReviewRequest.For(version).Value;

        var review = Answer(Hint(1, ContentReviewAnswerKind.Gap, "Schritt 3 fehlt.")).ToReview(request).Value;

        review.Reviewed.ShouldBe(ContentReviewVersions.TextChapterIds(version));
    }

    [Fact]
    public void No_hints_is_a_valid_review()
    {
        var version = ContentReviewVersions.WithTextChapters("Händehygiene", "Flächen");
        var request = ContentReviewRequest.For(version).Value;

        var result = Answer().ToReview(request);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Hints.ShouldBeEmpty();
        result.Value.Reviewed.ShouldBe(ContentReviewVersions.TextChapterIds(version));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_chapter_number_outside_the_chapters_sent_is_malformed_as_unknown_chapter(int chapter)
    {
        // Two text chapters and a PDF third: 3 is the PDF's position in the version, but only two chapters were sent.
        var version = ContentReviewVersions.WithTextChapters("Händehygiene", "Flächen");
        version.AddChapter(ContentReviewVersions.PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        var request = ContentReviewRequest.For(version).Value;
        var answer = Answer(Hint(1, ContentReviewAnswerKind.Gap, "Schritt 3 fehlt."), Hint(chapter, ContentReviewAnswerKind.Gap, "Schritt 4 fehlt."));

        Malformed(answer.ToReview(request), ContentReviewDefect.UnknownChapter);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("empty")]
    [InlineData("nul")]
    [InlineData("overlong")]
    public void A_blank_nul_or_overlong_hint_text_is_malformed_as_invalid_hint_text(string kind)
    {
        var request = Request("Händehygiene");
        var answer = Answer(Hint(1, ContentReviewAnswerKind.Gap, "Schritt 3 fehlt."), Hint(1, ContentReviewAnswerKind.Readability, Bad(kind)));

        Malformed(answer.ToReview(request), ContentReviewDefect.InvalidHintText);
    }

    [Fact]
    public void A_hint_text_of_the_maximum_length_is_accepted_also_with_whitespace_around_it()
    {
        var request = Request("Händehygiene");
        var longest = new string('x', ContentHint.MaxTextLength);
        ContentHint.MaxTextLength.ShouldBe(1000);

        var review = Answer(Hint(1, ContentReviewAnswerKind.Readability, $"  {longest}\n")).ToReview(request).Value;

        review.Hints.ShouldHaveSingleItem().Text.ShouldBe(longest);
    }

    [Fact]
    public void Six_hints_on_one_chapter_are_malformed_as_too_many_hints_and_five_are_not()
    {
        var request = Request("Händehygiene", "Flächen");
        ContentReviewAnswer.MaxHintsPerChapter.ShouldBe(5);

        Malformed(Answer(Hints(1, ContentReviewAnswer.MaxHintsPerChapter + 1)).ToReview(request), ContentReviewDefect.TooManyHints);
        Answer(Hints(1, ContentReviewAnswer.MaxHintsPerChapter)).ToReview(request).Value.Hints.Count.ShouldBe(ContentReviewAnswer.MaxHintsPerChapter);
        Answer([.. Hints(1, ContentReviewAnswer.MaxHintsPerChapter), .. Hints(2, ContentReviewAnswer.MaxHintsPerChapter)]).ToReview(request).Value.Hints.Count
            .ShouldBe(2 * ContentReviewAnswer.MaxHintsPerChapter, "the limit is per chapter, not per answer");
    }

    [Fact]
    public void Duplicate_hints_are_accepted()
    {
        var request = Request("Händehygiene");
        var hint = Hint(1, ContentReviewAnswerKind.Gap, "Schritt 3 fehlt.");

        var review = Answer(hint, hint).ToReview(request).Value;

        review.Hints.Count.ShouldBe(2);
        review.Hints[1].ShouldBe(review.Hints[0]);
    }

    [Fact]
    public void Every_answer_kind_maps_to_the_hint_kind_of_its_name()
    {
        var request = Request("Händehygiene");
        var kinds = Enum.GetValues<ContentReviewAnswerKind>();

        var review = Answer([.. kinds.Select(kind => Hint(1, kind, kind.ToString()))]).ToReview(request).Value;

        review.Hints.Select(hint => hint.Kind.ToString()).ShouldBe(kinds.Select(kind => kind.ToString()));
    }

    internal static ContentReviewAnswer Answer(params ContentReviewAnswerHint[] hints) => new(hints);

    internal static ContentReviewAnswerHint Hint(int chapter, ContentReviewAnswerKind kind, string text) => new(chapter, kind, text);

    private static ContentReviewAnswerHint[] Hints(int chapter, int count) =>
        [.. Enumerable.Range(1, count).Select(index => Hint(chapter, ContentReviewAnswerKind.Readability, $"Satz {index} ist zu lang."))];

    private static ContentReviewRequest Request(params string[] chapters) =>
        ContentReviewRequest.For(ContentReviewVersions.WithTextChapters(chapters)).Value;

    private static string Bad(string kind) => kind switch
    {
        "blank" => " \t\n ",
        "empty" => string.Empty,
        "nul" => "Schritt\0 fehlt.",
        "overlong" => new string('x', ContentHint.MaxTextLength + 1),
        _ => throw new System.Diagnostics.UnreachableException(kind),
    };
}
