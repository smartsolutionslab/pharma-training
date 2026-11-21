using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class RemoveChapterTests
{
    [Fact]
    public async Task A_chapter_is_removed_from_a_draft_once_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var removed = await Handler(store).HandleAsync(new RemoveChapter(version.Id, first), TestContext.Current.CancellationToken);

        removed.IsSuccess.ShouldBeTrue();
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(second);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var removed = await Handler(store).HandleAsync(new RemoveChapter(CourseVersionId.New(), ChapterId.New()), TestContext.Current.CancellationToken);

        removed.Error.Code.ShouldBe("editorial.course-version-not-found");
        removed.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_the_version_does_not_hold_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var removed = await Handler(store).HandleAsync(new RemoveChapter(version.Id, ChapterId.New()), TestContext.Current.CancellationToken);

        removed.Error.Code.ShouldBe("editorial.chapter-not-found");
        removed.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        version.Chapters.ShouldHaveSingleItem();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_of_a_published_version_stays()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var removed = await Handler(store).HandleAsync(new RemoveChapter(version.Id, chapter), TestContext.Current.CancellationToken);

        removed.Error.Code.ShouldBe("editorial.not-a-draft");
        removed.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(chapter);
        store.Commits.ShouldBe(0);
    }

    private static RemoveChapterHandler Handler(InMemoryCourseVersionStore store) => new(store, store);
}
