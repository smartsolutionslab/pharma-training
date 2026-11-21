using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class ReplaceTextChapterTests
{
    [Fact]
    public async Task A_text_chapter_of_a_draft_gets_its_new_title_and_text_in_its_place_once_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var replaced = await Handler(store).HandleAsync(Command(version.Id, first), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue();
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([first, second]);
        version.Chapters[0].Title.ShouldBe(ChapterTitle.From("Händehygiene, überarbeitet"));
        version.Chapters[0].Text.ShouldBe(MarkdownText.From("Neu: **vor** und nach …"));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var replaced = await Handler(store).HandleAsync(Command(CourseVersionId.New(), ChapterId.New()), TestContext.Current.CancellationToken);

        replaced.Error.Code.ShouldBe("editorial.course-version-not-found");
        replaced.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_the_version_does_not_hold_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var replaced = await Handler(store).HandleAsync(Command(version.Id, ChapterId.New()), TestContext.Current.CancellationToken);

        replaced.Error.Code.ShouldBe("editorial.chapter-not-found");
        replaced.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_of_a_published_version_stays_as_it_was()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var replaced = await Handler(store).HandleAsync(Command(version.Id, chapter), TestContext.Current.CancellationToken);

        replaced.Error.Code.ShouldBe("editorial.not-a-draft");
        replaced.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Chapters.Single().Title.ShouldBe(ChapterTitle.From("Händehygiene"));
        store.Commits.ShouldBe(0);
    }

    private static ReplaceTextChapterHandler Handler(InMemoryCourseVersionStore store) => new(store, store);

    private static ReplaceTextChapter Command(CourseVersionId version, ChapterId chapter) =>
        new(version, chapter, ChapterTitle.From("Händehygiene, überarbeitet"), MarkdownText.From("Neu: **vor** und nach …"));
}
