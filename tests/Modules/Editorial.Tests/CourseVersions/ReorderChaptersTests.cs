using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReorderChapters;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class ReorderChaptersTests
{
    [Fact]
    public async Task The_chapters_of_a_draft_take_the_order_given_once_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var reordered = await Handler(store).HandleAsync(new ReorderChapters(version.Id, [second, first]), TestContext.Current.CancellationToken);

        reordered.IsSuccess.ShouldBeTrue();
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([second, first]);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_order_that_misses_a_chapter_is_a_conflict_and_changes_nothing()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var reordered = await Handler(store).HandleAsync(new ReorderChapters(version.Id, [second]), TestContext.Current.CancellationToken);

        reordered.Error.Code.ShouldBe("editorial.order-does-not-match");
        reordered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([first, second]);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var reordered = await Handler(store).HandleAsync(new ReorderChapters(CourseVersionId.New(), [ChapterId.New()]), TestContext.Current.CancellationToken);

        reordered.Error.Code.ShouldBe("editorial.course-version-not-found");
        reordered.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task The_chapters_of_a_published_version_keep_their_order()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var reordered = await Handler(store).HandleAsync(new ReorderChapters(version.Id, [chapter]), TestContext.Current.CancellationToken);

        reordered.Error.Code.ShouldBe("editorial.not-a-draft");
        reordered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
    }

    private static ReorderChaptersHandler Handler(InMemoryCourseVersionStore store) => new(store, store);
}
