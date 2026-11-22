using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class RemoveQuestionTests
{
    [Fact]
    public async Task A_question_of_a_draft_is_gone_once_committed_and_the_others_keep_their_order()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithQuestion(out var chapter, out var first);
        var second = version.AddQuestion(QuestionId.New(), MultipleChoice(chapter), AnswerOptionId.New).Value;
        var third = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        store.Stored.Add(version);

        var removed = await Handler(store).HandleAsync(new RemoveQuestion(version.Id, second), TestContext.Current.CancellationToken);

        removed.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe([first, third]);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var removed = await Handler(store).HandleAsync(new RemoveQuestion(CourseVersionId.New(), QuestionId.New()), TestContext.Current.CancellationToken);

        removed.Error.Code.ShouldBe("editorial.course-version-not-found");
        removed.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_the_version_does_not_hold_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithQuestion(out _, out var question);
        store.Stored.Add(version);

        var removed = await Handler(store).HandleAsync(new RemoveQuestion(version.Id, QuestionId.New()), TestContext.Current.CancellationToken);

        removed.Error.Code.ShouldBe("editorial.question-not-found");
        removed.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        version.Questions.Select(kept => kept.Id).ShouldBe([question]);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_of_a_published_version_stays()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published();
        var question = version.Questions.ShouldHaveSingleItem().Id;
        store.Stored.Add(version);

        var removed = await Handler(store).HandleAsync(new RemoveQuestion(version.Id, question), TestContext.Current.CancellationToken);

        removed.Error.Code.ShouldBe("editorial.not-a-draft");
        removed.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Questions.Select(kept => kept.Id).ShouldBe([question]);
        store.Commits.ShouldBe(0);
    }

    private static RemoveQuestionHandler Handler(InMemoryCourseVersionStore store) => new(store, store);
}
