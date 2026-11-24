using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.EditQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class EditQuestionTests
{
    [Fact]
    public async Task An_approved_question_that_is_edited_gets_its_new_content_and_needs_a_review_again()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithApprovedQuestion(out var chapter, out var id);
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, id, MultipleChoice(chapter), Author), TestContext.Current.CancellationToken);

        edited.IsSuccess.ShouldBeTrue();
        var question = version.Questions.ShouldHaveSingleItem();
        question.Id.ShouldBe(id);
        question.Text.ShouldBe(QuestionText.From("Welche Flächen werden desinfiziert?"));
        question.Type.ShouldBe(QuestionType.MultipleChoice);
        question.Options.Select(option => (option.Text.Value, option.Correctness)).ShouldBe(
        [
            ("Rezepturtisch", Correctness.Correct),
            ("Waage", Correctness.Correct),
            ("Schaufenster", Correctness.Incorrect),
        ]);
        question.Provenance.ShouldBe(Provenance.Human);
        question.Review.ShouldBe(ReviewState.Unreviewed);
        question.ReviewedBy.ShouldBeNull();
        question.ReviewedAt.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_ai_drafted_question_stays_ai_drafted_after_a_person_edits_it()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var chapter);
        version.ApplyAiTestDraft([SingleChoice(chapter)], QuestionId.New, AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        var id = version.Questions.ShouldHaveSingleItem().Id;
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, id, MultipleChoice(chapter), Author), TestContext.Current.CancellationToken);

        edited.IsSuccess.ShouldBeTrue();
        version.Questions.ShouldHaveSingleItem().Provenance.ShouldBe(Provenance.AiDrafted);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var edited = await Handler(store).HandleAsync(new EditQuestion(CourseVersionId.New(), QuestionId.New(), SingleChoice(ChapterId.New()), Author), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("editorial.course-version-not-found");
        edited.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_the_version_does_not_hold_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithQuestion(out var chapter, out _);
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, QuestionId.New(), MultipleChoice(chapter), Author), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("editorial.question-not-found");
        edited.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_of_a_published_version_stays_as_it_was()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out var chapter);
        var question = version.Questions.ShouldHaveSingleItem();
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, question.Id, MultipleChoice(chapter), Author), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("editorial.not-a-draft");
        edited.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        question.Type.ShouldBe(QuestionType.SingleChoice);
        question.Review.ShouldBe(ReviewState.Approved);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_published_version_is_refused_as_not_a_draft_before_the_chapter_is_looked_for()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out _);
        var question = version.Questions.ShouldHaveSingleItem();
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, question.Id, MultipleChoice(ChapterId.New()), Author), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("editorial.not-a-draft");
        edited.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        question.Type.ShouldBe(QuestionType.SingleChoice);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_question_is_not_found_before_the_chapter_is_looked_for()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithQuestion(out _, out _);
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, QuestionId.New(), MultipleChoice(ChapterId.New()), Author), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("editorial.question-not-found");
        edited.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_of_a_draft_moved_to_a_chapter_the_version_does_not_hold_is_refused_as_chapter_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithQuestion(out _, out var id);
        var question = version.Questions.ShouldHaveSingleItem();
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, id, MultipleChoice(ChapterId.New()), Author), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("editorial.chapter-not-found");
        edited.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        question.Type.ShouldBe(QuestionType.SingleChoice);
        store.Commits.ShouldBe(0);
    }

    /// <summary>The reset names the editor the command carries, who need not be the one who approved (#246).</summary>
    [Fact]
    public async Task The_reset_names_the_commands_editor()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithApprovedQuestion(out var chapter, out var id);
        store.Stored.Add(version);

        var edited = await Handler(store).HandleAsync(new EditQuestion(version.Id, id, MultipleChoice(chapter), Reviewer), TestContext.Current.CancellationToken);

        edited.IsSuccess.ShouldBeTrue();
        version.DomainEvents.OfType<QuestionApprovalsReset>().ShouldHaveSingleItem()
            .ShouldBe(new QuestionApprovalsReset(version.Id, Reviewer, ApprovalResetReason.QuestionEdited));
        store.Commits.ShouldBe(1);
    }

    private static EditQuestionHandler Handler(InMemoryCourseVersionStore store) => new(store, store);
}
