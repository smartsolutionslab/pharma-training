using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>
/// What the AI test draft asks for, worked out from the version before the model is called: the text chapters only,
/// numbered in the version's order, and the blueprint's count less the questions the draft keeps (approved ones, and copied or
/// hand-written ones even while unreviewed, #245), split evenly over them.
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
        request.Chapters[0].Text.ShouldBe(MarkdownText.From("Handschuhe bei jeder Rezeptur wechseln."));
    }

    [Fact]
    public void Pdf_and_video_chapters_are_left_out()
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
        Chapter.OfPdf(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.pdf"));

    private static Chapter VideoChapter(string title) =>
        Chapter.OfVideo(ChapterId.New(), ChapterTitle.From(title), BlobReference.From($"chapters/{Guid.CreateVersion7()}.mp4"), VideoDuration.From(90));
}
