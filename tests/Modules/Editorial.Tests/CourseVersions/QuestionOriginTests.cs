using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Every question records how it came into its version (#245): drafted by the AI, copied from the published version,
/// or written by hand. Applying an AI draft replaces only the AI-drafted ones that are unreviewed (the owner's decision of
/// 2026-11-23), untouched by an editor and never approved (the owner's decision of 2026-11-24); copied and hand-written
/// questions stay, reviewed or not, and only need approving again.
/// </summary>
public class QuestionOriginTests
{
    [Fact]
    public void After_a_settings_change_an_ai_draft_keeps_every_copied_and_hand_written_question()
    {
        var next = Published().DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        var chapter = next.Chapters.ShouldHaveSingleItem().Id;
        var copied = next.Questions.ShouldHaveSingleItem().Id;
        var handWritten = next.AddQuestion(QuestionId.New(), MultipleChoice(chapter), AnswerOptionId.New).Value;
        var earlierAi = next.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(4), ScorePercent.From(90), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        next.Questions.ShouldAllBe(question => question.Review == ReviewState.Unreviewed);
        var keptBefore = Contents(next, copied, handWritten);

        var applied = next.ApplyAiTestDraft([SingleChoice(chapter), MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        next.Questions.Select(question => question.Id).ShouldBe([copied, handWritten, .. applied.Value]);
        next.Questions.ShouldNotContain(question => question.Id == earlierAi);
        next.Questions.Select(question => question.Origin).ShouldBe([QuestionOrigin.Copied, QuestionOrigin.HandWritten, QuestionOrigin.AiDraft, QuestionOrigin.AiDraft]);
        Contents(next, copied, handWritten).ShouldBe(keptBefore);
        next.Questions.ShouldAllBe(question => question.Review == ReviewState.Unreviewed);
    }

    [Fact]
    public void A_question_written_by_hand_is_hand_written()
    {
        var version = DraftWithQuestion(out _, out _);

        var question = version.Questions.ShouldHaveSingleItem();

        question.Origin.ShouldBe(QuestionOrigin.HandWritten);
        question.Provenance.ShouldBe(Provenance.Human);
    }

    [Fact]
    public void A_question_the_ai_drafted_is_ai_drafted()
    {
        var version = DraftWithChapter(out var chapter);

        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();

        var question = version.Questions.Single(question => question.Id == drafted);
        question.Origin.ShouldBe(QuestionOrigin.AiDraft);
        question.Provenance.ShouldBe(Provenance.AiDrafted);
    }

    [Fact]
    public void Drafting_the_next_version_marks_every_question_copied_and_keeps_its_provenance()
    {
        var published = PublishedWithAHandWrittenAndAnAiDraftedQuestion();

        var next = published.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;

        next.Questions.Select(question => question.Origin).ShouldBe([QuestionOrigin.Copied, QuestionOrigin.Copied]);
        next.Questions.Select(question => question.Provenance).ShouldBe([Provenance.Human, Provenance.AiDrafted]);
        published.Questions.Select(question => question.Origin).ShouldBe([QuestionOrigin.HandWritten, QuestionOrigin.AiDraft]);
    }

    [Fact]
    public void A_copied_question_that_was_ai_drafted_before_is_kept_by_a_draft_while_unreviewed()
    {
        var next = PublishedWithAHandWrittenAndAnAiDraftedQuestion()
            .DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        var chapter = next.Chapters.ShouldHaveSingleItem().Id;
        List<QuestionId> copied = [.. next.Questions.Select(question => question.Id)];
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(3), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();

        var applied = next.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        next.Questions.Select(question => question.Id).ShouldBe([.. copied, .. applied.Value]);
    }

    [Fact]
    public void An_unreviewed_ai_drafted_question_is_replaced_and_an_approved_one_stays()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value;
        version.ApproveQuestion(drafted[0], Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();

        var applied = version.ApplyAiTestDraft([MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe([drafted[0], .. applied.Value]);
    }

    /// <summary>
    /// An editor's change is human work (the owner's decision of 2026-11-24): the question stays AI-drafted in origin and
    /// provenance, records the edit, and the next draft keeps it although it is unreviewed again.
    /// </summary>
    [Fact]
    public void An_ai_drafted_question_an_editor_edited_survives_the_next_draft()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        version.EditQuestion(drafted, MultipleChoice(chapter), Author, AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        var edited = version.Questions.ShouldHaveSingleItem();
        edited.Origin.ShouldBe(QuestionOrigin.AiDraft);
        edited.EditHistory.ShouldBe(EditHistory.Edited);
        edited.Review.ShouldBe(ReviewState.Unreviewed);

        var applied = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe([drafted, .. applied.Value]);
    }

    /// <summary>
    /// An approval is human work for good (the owner's decision of 2026-11-24): a settings change sends the question back
    /// to review (#199), but it stays approved once, and the next draft keeps it.
    /// </summary>
    [Fact]
    public void An_ai_drafted_question_approved_once_survives_the_next_draft_after_a_settings_change_reset_it()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        version.ApproveQuestion(drafted, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(3), ScorePercent.From(90), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var reset = version.Questions.ShouldHaveSingleItem();
        reset.Review.ShouldBe(ReviewState.Unreviewed);
        reset.ApprovalHistory.ShouldBe(ApprovalHistory.ApprovedOnce);

        var applied = version.ApplyAiTestDraft([SingleChoice(chapter), MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe([drafted, .. applied.Value]);
    }

    [Fact]
    public void An_ai_drafted_question_approved_once_survives_the_next_draft_after_a_chapter_change_reset_it()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        version.ApproveQuestion(drafted, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.ReplaceChapter(TextChapter(chapter, "Geändert"), Author).IsSuccess.ShouldBeTrue();
        version.Questions.ShouldHaveSingleItem().Review.ShouldBe(ReviewState.Unreviewed);

        var applied = version.ApplyAiTestDraft([MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe([drafted, .. applied.Value]);
    }

    [Fact]
    public void An_untouched_ai_drafted_question_never_approved_is_replaced_by_the_next_draft()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        var untouched = version.Questions.ShouldHaveSingleItem();
        untouched.EditHistory.ShouldBe(EditHistory.Untouched);
        untouched.ApprovalHistory.ShouldBe(ApprovalHistory.NeverApproved);

        var applied = version.ApplyAiTestDraft([MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe(applied.Value);
        version.Questions.ShouldNotContain(question => question.Id == drafted);
    }

    [Fact]
    public void A_new_question_is_untouched_and_never_approved()
    {
        var question = DraftWithQuestion(out _, out _).Questions.ShouldHaveSingleItem();

        question.EditHistory.ShouldBe(EditHistory.Untouched);
        question.ApprovalHistory.ShouldBe(ApprovalHistory.NeverApproved);
    }

    [Fact]
    public void An_edit_after_an_approval_keeps_the_question_approved_once()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        version.ApproveQuestion(drafted, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();

        version.EditQuestion(drafted, MultipleChoice(chapter), Author, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Unreviewed);
        question.EditHistory.ShouldBe(EditHistory.Edited);
        question.ApprovalHistory.ShouldBe(ApprovalHistory.ApprovedOnce);
    }

    /// <summary>A copy keeps the question's history, as it keeps its provenance and its approval.</summary>
    [Fact]
    public void Drafting_the_next_version_keeps_each_question_s_edit_and_approval_history()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(2), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        List<QuestionId> drafted = [.. version.ApplyAiTestDraft([SingleChoice(chapter), SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value];
        version.EditQuestion(drafted[0], SingleChoice(chapter), Author, AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        drafted.ForEach(id => version.ApproveQuestion(id, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue());
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();

        var next = version.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;

        next.Questions.Select(question => question.EditHistory).ShouldBe([EditHistory.Edited, EditHistory.Untouched]);
        next.Questions.Select(question => question.ApprovalHistory).ShouldBe([ApprovalHistory.ApprovedOnce, ApprovalHistory.ApprovedOnce]);
    }

    [Fact]
    public void Questions_a_draft_keeps_count_toward_the_limit()
    {
        var version = DraftWithChapter(out var chapter);
        for (var index = 0; index < QuestionCount.Maximum; index++)
        {
            version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        }

        var refused = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New);

        refused.IsFailure.ShouldBeTrue("the unreviewed hand-written questions stay, so the draft would exceed the limit");
        refused.Error.ShouldBeOfType<TooManyQuestions>();
        version.Questions.Count.ShouldBe(QuestionCount.Maximum);
        version.Questions.ShouldAllBe(question => question.Origin == QuestionOrigin.HandWritten);
    }

    /// <summary>A published version of one chapter holding a hand-written question, then an AI-drafted one, both approved.</summary>
    private static CourseVersion PublishedWithAHandWrittenAndAnAiDraftedQuestion()
    {
        var version = DraftWithChapter(out var chapter);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(2), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var handWritten = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(handWritten, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        var drafted = version.ApplyAiTestDraft([MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        version.ApproveQuestion(drafted, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static IReadOnlyList<string> Contents(CourseVersion version, params QuestionId[] ids) =>
        [.. version.Questions.Where(question => ids.Contains(question.Id)).Select(question =>
            $"{question.Id}|{question.Text}|{question.Type}|{question.Chapter}|{question.Provenance}|{question.Origin}|"
            + string.Join(";", question.Options.Select(option => $"{option.Id}:{option.Text}:{option.Correctness}")))];
}
