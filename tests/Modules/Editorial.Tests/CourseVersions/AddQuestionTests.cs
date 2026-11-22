using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class AddQuestionTests
{
    [Fact]
    public async Task A_question_is_added_to_a_draft_as_a_persons_and_unreviewed_once_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(new AddQuestion(version.Id, MultipleChoice(chapter)), TestContext.Current.CancellationToken);

        added.IsSuccess.ShouldBeTrue();
        var question = version.Questions.ShouldHaveSingleItem();
        question.Id.Value.ShouldBe(added.Value);
        question.Text.ShouldBe(QuestionText.From("Welche Flächen werden desinfiziert?"));
        question.Type.ShouldBe(QuestionType.MultipleChoice);
        question.Chapter.ShouldBe(chapter);
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
    public async Task Questions_are_added_after_the_ones_the_draft_already_holds()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithQuestion(out var chapter, out var first);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(new AddQuestion(version.Id, MultipleChoice(chapter)), TestContext.Current.CancellationToken);

        version.Questions.Select(question => question.Id.Value).ShouldBe([first.Value, added.Value]);
    }

    [Fact]
    public async Task A_question_a_draft_may_not_yet_approve_is_added_all_the_same()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(new AddQuestion(version.Id, TwoCorrectSingleChoice(chapter)), TestContext.Current.CancellationToken);

        added.IsSuccess.ShouldBeTrue();
        version.Questions.ShouldHaveSingleItem().Review.ShouldBe(ReviewState.Unreviewed);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var added = await Handler(store).HandleAsync(new AddQuestion(CourseVersionId.New(), SingleChoice(ChapterId.New())), TestContext.Current.CancellationToken);

        added.Error.Code.ShouldBe("editorial.course-version-not-found");
        added.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_about_a_chapter_the_version_does_not_hold_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(new AddQuestion(version.Id, SingleChoice(ChapterId.New())), TestContext.Current.CancellationToken);

        added.Error.Code.ShouldBe("editorial.chapter-not-found");
        added.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        version.Questions.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_published_version_takes_no_question()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(new AddQuestion(version.Id, MultipleChoice(chapter)), TestContext.Current.CancellationToken);

        added.Error.Code.ShouldBe("editorial.not-a-draft");
        added.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Questions.Count.ShouldBe(1);
        store.Commits.ShouldBe(0);
    }

    private static AddQuestionHandler Handler(InMemoryCourseVersionStore store) => new(store, store);
}
