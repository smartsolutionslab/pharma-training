using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Every question records how it came into its version (#245): drafted by the AI, copied from the published version,
/// or written by hand. Applying an AI draft replaces only the unreviewed AI-drafted ones (the owner's decision of
/// 2026-11-23); copied and hand-written questions stay, reviewed or not, and only need approving again.
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
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(4), ScorePercent.From(90), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
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
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(3), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();

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

    /// <summary>An editor's change does not make a question hand-written: it came from the AI draft, and its provenance says so too.</summary>
    [Fact]
    public void An_ai_drafted_question_an_editor_changed_stays_ai_drafted()
    {
        var version = DraftWithChapter(out var chapter);
        var drafted = version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();

        version.EditQuestion(drafted, MultipleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        version.Questions.ShouldHaveSingleItem().Origin.ShouldBe(QuestionOrigin.AiDraft);
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
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(2), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
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
