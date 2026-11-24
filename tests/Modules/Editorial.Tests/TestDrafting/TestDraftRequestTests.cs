using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>
/// What the AI test draft asks for, worked out from the version before the model is called: the chapters with text (a
/// text chapter's Markdown, a PDF's extracted text), numbered together in the version's order, and the blueprint's
/// count less the questions the draft keeps (approved ones, copied or hand-written ones even while unreviewed, and
/// AI-drafted ones an editor edited or approved once, #245), split evenly over them.
/// </summary>
public class TestDraftRequestTests
{
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

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Number).ShouldBe([1, 2, 3]);
        request.Chapters.Select(chapter => chapter.Id).ShouldBe([third, first, second]);
        request.Chapters[0].Title.ShouldBe(ChapterTitle.From("Handschuhe"));
        request.Chapters[0].Source.ShouldBe(TestDraftSource.Text);
        request.Chapters[0].Text.ShouldBe("Handschuhe bei jeder Rezeptur wechseln.");
    }

    [Fact]
    public void Pdf_chapters_without_text_and_video_chapters_are_left_out()
    {
        var version = Draft();
        var text = ChapterId.New();
        var otherText = ChapterId.New();
        version.AddChapter(TextChapter(text, "Händehygiene")).IsSuccess.ShouldBeTrue();
        version.AddChapter(PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(otherText, "Flächen")).IsSuccess.ShouldBeTrue();
        version.AddChapter(VideoChapter("Lehrvideo")).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Id).ShouldBe([text, otherText]);
        request.Chapters.Select(chapter => chapter.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void Text_and_pdf_chapters_with_text_are_numbered_together_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(6, "TPVNTR", out var ids);

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Number).ShouldBe([1, 2, 3, 4]);
        request.Chapters.Select(chapter => chapter.Id).ShouldBe([ids[0], ids[1], ids[4], ids[5]]);
        request.Chapters.Select(chapter => chapter.Source).ShouldBe([TestDraftSource.Text, TestDraftSource.Pdf, TestDraftSource.Text, TestDraftSource.Pdf]);
        request.Chapters.Select(chapter => chapter.Text).ShouldBe([TestDraftVersions.TextOf('T', 1), TestDraftVersions.TextOf('P', 2), TestDraftVersions.TextOf('T', 5), TestDraftVersions.TextOf('R', 6)]);
        request.Chapters[1].Title.ShouldBe(ChapterTitle.From("PDF-Kapitel 2"));
        request.LeftOutChapters.ShouldBe([ids[2], ids[3]]);
        request.TruncatedChapters.ShouldBe([ids[5]]);
    }

    [Theory]
    [InlineData(10, "TPPT", new[] { 3, 3, 2, 2 })]
    [InlineData(10, "PVTNPLT", new[] { 3, 3, 2, 2 })]
    [InlineData(2, "PTR", new[] { 1, 1, 0 })]
    [InlineData(6, "PPP", new[] { 2, 2, 2 })]
    [InlineData(5, "RVT", new[] { 3, 2 })]
    public void The_count_is_split_evenly_over_text_and_pdf_chapters_alike_and_left_out_chapters_count_for_nothing(int questions, string layout, int[] quotas)
    {
        var version = TestDraftVersions.WithChapters(questions, layout, out _);

        var request = TestDraftRequest.For(version).Value;

        request.Questions.ShouldBe(QuestionCount.From(questions));
        request.Chapters.Select(chapter => chapter.Quota.Value).ShouldBe(quotas);
    }

    [Fact]
    public void A_version_of_pdf_chapters_with_text_only_is_drafted()
    {
        var version = TestDraftVersions.WithChapters(5, "PP", out var ids);

        var result = TestDraftRequest.For(version);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        result.Value.Chapters.Select(chapter => chapter.Id).ShouldBe(ids);
        result.Value.Chapters.ShouldAllBe(chapter => chapter.Source == TestDraftSource.Pdf);
        result.Value.Chapters.Select(chapter => chapter.Quota.Value).ShouldBe([3, 2]);
        result.Value.LeftOutChapters.ShouldBeEmpty();
        result.Value.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void Videos_pdfs_without_text_and_pdfs_recorded_before_extraction_are_left_out_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(3, "LTVNPL", out var ids);

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Id).ShouldBe([ids[1], ids[4]]);
        request.LeftOutChapters.ShouldBe([ids[0], ids[2], ids[3], ids[5]]);
        request.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void Pdf_chapters_cut_at_a_limit_are_drafted_and_named_as_truncated_in_the_versions_order()
    {
        var version = TestDraftVersions.WithChapters(4, "RTPR", out var ids);

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Select(chapter => chapter.Id).ShouldBe(ids);
        request.TruncatedChapters.ShouldBe([ids[0], ids[3]]);
        request.LeftOutChapters.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("NV")]
    [InlineData("L")]
    [InlineData("NLV")]
    public void A_version_whose_pdfs_have_no_text_is_refused_with_no_text_chapters(string layout)
    {
        var version = TestDraftVersions.WithChapters(3, layout, out _);

        var result = TestDraftRequest.For(version);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.no-text-chapters");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_pdf_whose_text_would_pass_the_limit_is_cut_to_what_is_left_and_named_as_truncated()
    {
        // A text chapter "A" of the longest Markdown and a PDF chapter "B" whose text is one character longer than what
        // the limit leaves it: titles and texts of both kinds count alike, and the PDF gives way.
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        var left = TestDraftRequest.MaxCharacters - MarkdownText.MaxLength - 2;
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "B", new string('b', left + 1))).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Single(chapter => chapter.Id == pdf).Text.Length.ShouldBe(left);
        Characters(request).ShouldBe(TestDraftRequest.MaxCharacters);
        request.TruncatedChapters.ShouldBe([pdf]);
    }

    [Fact]
    public void A_pdf_that_fits_what_is_left_is_sent_whole_and_not_named()
    {
        var version = Draft();
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        var left = TestDraftRequest.MaxCharacters - MarkdownText.MaxLength - 2;
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "B", new string('b', left))).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.Single(chapter => chapter.Id == pdf).Text.Length.ShouldBe(left);
        request.TruncatedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void Two_pdfs_of_the_longest_text_share_the_limit_evenly_and_the_version_gets_a_draft()
    {
        // #157's promise: long PDFs are cut and named, never the reason a version gets no draft.
        var version = Draft();
        var first = ChapterId.New();
        var second = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(first, "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TestDraftVersions.PdfWithText(second, "B", new string('b', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        var share = (TestDraftRequest.MaxCharacters - 2) / 2;
        request.Chapters.Select(chapter => chapter.Text.Length).ShouldBe([share, share]);
        Characters(request).ShouldBeLessThanOrEqualTo(TestDraftRequest.MaxCharacters);
        request.TruncatedChapters.ShouldBe([first, second]);
    }

    [Fact]
    public void A_short_pdf_is_sent_whole_and_the_long_ones_share_what_it_leaves()
    {
        var version = Draft();
        var longFirst = ChapterId.New();
        var shortOne = ChapterId.New();
        var longSecond = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(longFirst, "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TestDraftVersions.PdfWithText(shortOne, "B", new string('b', 1_000))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TestDraftVersions.PdfWithText(longSecond, "C", new string('c', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        var lengths = request.Chapters.Select(chapter => chapter.Text.Length).ToList();
        lengths[1].ShouldBe(1_000);
        Math.Abs(lengths[0] - lengths[2]).ShouldBeLessThanOrEqualTo(1, "the long PDFs share what is left evenly");
        Characters(request).ShouldBe(TestDraftRequest.MaxCharacters);
        request.TruncatedChapters.ShouldBe([longFirst, longSecond]);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var version = Draft();
        // The cut falls between the two halves of an emoji: the whole pair stays out.
        version.AddChapter(TextChapter(ChapterId.New(), "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', 100))).IsSuccess.ShouldBeTrue();
        var left = TestDraftRequest.MaxCharacters - MarkdownText.MaxLength - 100 - 3;
        var pdf = ChapterId.New();
        var text = new string('c', left - 1) + "\U0001F600" + "cc";
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "C", text)).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        var sent = request.Chapters.Single(chapter => chapter.Id == pdf).Text;
        sent.ShouldBe(new string('c', left - 1));
        request.TruncatedChapters.ShouldBe([pdf]);
    }

    [Fact]
    public void A_pdf_the_text_chapters_leave_no_room_for_is_left_out_and_named()
    {
        // The text chapters take the limit to the last character: the PDF's text has nowhere to go, so it is left out
        // like a PDF without text, and the version is drafted from its text chapters alone.
        var version = Draft();
        var text = ChapterId.New();
        version.AddChapter(TextChapter(text, "A", new string('a', MarkdownText.MaxLength))).IsSuccess.ShouldBeTrue();
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', TestDraftRequest.MaxCharacters - MarkdownText.MaxLength - 3))).IsSuccess.ShouldBeTrue();
        var pdf = ChapterId.New();
        version.AddChapter(TestDraftVersions.PdfWithText(pdf, "C", new string('c', 1_000))).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        request.Chapters.ShouldNotContain(chapter => chapter.Id == pdf);
        request.LeftOutChapters.ShouldBe([pdf]);
        request.TruncatedChapters.ShouldBeEmpty();
        Characters(request).ShouldBeLessThanOrEqualTo(TestDraftRequest.MaxCharacters);
    }

    private static long Characters(TestDraftRequest request) =>
        request.Chapters.Sum(chapter => (long)chapter.Title.Value.Length + chapter.Text.Length);

    [Theory]
    [InlineData(10, 4, new[] { 3, 3, 2, 2 })]
    [InlineData(6, 3, new[] { 2, 2, 2 })]
    [InlineData(2, 3, new[] { 1, 1, 0 })]
    public void The_blueprints_count_is_split_evenly_with_the_remainder_on_the_first_chapters(int questions, int chapters, int[] quotas)
    {
        var version = Draft();
        for (var index = 0; index < chapters; index++)
        {
            version.AddChapter(TextChapter(ChapterId.New(), $"Kapitel {index + 1}")).IsSuccess.ShouldBeTrue();
        }

        version.SetBlueprint(Blueprint(questions)).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        request.Questions.ShouldBe(QuestionCount.From(questions));
        request.Chapters.Select(chapter => chapter.Quota.Value).ShouldBe(quotas);
    }

    [Fact]
    public void Questions_the_draft_keeps_are_subtracted_and_unreviewed_ai_drafted_ones_are_not()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(Blueprint(6)).IsSuccess.ShouldBeTrue();
        var approved = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(approved, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        var approvedAi = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        version.ApproveQuestion(approvedAi, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var request = TestDraftRequest.For(version).Value;

        request.Questions.ShouldBe(QuestionCount.From(3), "two approved and one unreviewed hand-written question stay; the unreviewed AI-drafted one is replaced");
        request.Chapters.ShouldHaveSingleItem().Quota.ShouldBe(QuestionCount.From(3));
    }

    [Fact]
    public void Ai_drafted_questions_an_editor_edited_or_approved_once_are_subtracted_as_kept()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(Blueprint(5)).IsSuccess.ShouldBeTrue();
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter), SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value;
        version.EditQuestion(drafted[0], SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(drafted[1], Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(Blueprint(6)).IsSuccess.ShouldBeTrue();
        version.Questions.ShouldAllBe(question => question.Review == ReviewState.Unreviewed);

        var request = TestDraftRequest.For(version).Value;

        request.Questions.ShouldBe(QuestionCount.From(4), "the edited and the once-approved AI-drafted questions stay; only the untouched one is replaced");
    }

    [Fact]
    public void Copied_and_hand_written_questions_that_fill_the_blueprint_after_a_settings_change_leave_nothing_to_draft()
    {
        var next = Published().DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        var chapter = next.Chapters.ShouldHaveSingleItem().Id;
        next.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        next.SetBlueprint(Blueprint(2)).IsSuccess.ShouldBeTrue();

        var result = TestDraftRequest.For(next);

        result.IsFailure.ShouldBeTrue("the copied and the hand-written question stay, unreviewed, and fill the blueprint");
        result.Error.Code.ShouldBe("editorial.nothing-to-draft");
    }

    [Fact]
    public void A_version_that_is_not_a_draft_is_refused_as_not_a_draft()
    {
        foreach (var version in new[] { InReview(), Published() })
        {
            var result = TestDraftRequest.For(version);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("editorial.not-a-draft");
            result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_version_without_a_text_chapter_is_refused_with_no_text_chapters(bool withFileChapters)
    {
        var version = Draft();
        if (withFileChapters)
        {
            version.AddChapter(PdfChapter("Merkblatt")).IsSuccess.ShouldBeTrue();
            version.AddChapter(VideoChapter("Lehrvideo")).IsSuccess.ShouldBeTrue();
        }

        var result = TestDraftRequest.For(version);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.no-text-chapters");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    public void Approved_questions_that_fill_the_blueprint_leave_nothing_to_draft(int blueprint, int approved)
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(Blueprint(blueprint)).IsSuccess.ShouldBeTrue();
        for (var index = 0; index < approved; index++)
        {
            var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
            version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        }

        var result = TestDraftRequest.For(version);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("editorial.nothing-to-draft");
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
        var rest = TestDraftRequest.MaxCharacters - MarkdownText.MaxLength - 2 + beyondTheLimit;
        version.AddChapter(TextChapter(ChapterId.New(), "B", new string('b', rest))).IsSuccess.ShouldBeTrue();

        var result = TestDraftRequest.For(version);

        result.IsFailure.ShouldBe(refused);
        if (refused)
        {
            result.Error.Code.ShouldBe("editorial.too-much-content-to-draft");
            result.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
    }

    [Fact]
    public void Each_new_failure_has_its_code_status_and_one_English_message()
    {
        Failure(CourseVersionFailures.NoTextChapters, "editorial.no-text-chapters", HttpStatusCode.Conflict);
        Failure(CourseVersionFailures.NothingToDraft, "editorial.nothing-to-draft", HttpStatusCode.Conflict);
        Failure(CourseVersionFailures.TooMuchContentToDraft, "editorial.too-much-content-to-draft", HttpStatusCode.UnprocessableEntity);
    }

    private static void Failure(Func<ApiError> failure, string code, HttpStatusCode status)
    {
        var error = failure();

        error.Code.ShouldBe(code);
        error.Status.ShouldBe(status);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        failure().Message.ShouldBe(error.Message, "a code has one message");
    }

    private static TestBlueprint Blueprint(int questions) =>
        new(QuestionCount.From(questions), ScorePercent.From(80), RecurrenceInterval.Yearly);

    private static Chapter PdfChapter(string title) =>
        Chapter.OfPdf(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.pdf"), PdfTextExtraction.NoText, null);

    private static Chapter VideoChapter(string title) =>
        Chapter.OfVideo(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.mp4"), VideoDuration.From(90));
}
