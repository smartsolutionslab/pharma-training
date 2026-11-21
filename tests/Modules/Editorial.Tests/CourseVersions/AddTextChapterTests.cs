using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class AddTextChapterTests
{
    [Fact]
    public async Task A_text_chapter_is_added_at_the_end_of_a_draft_committed_and_its_id_answered()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var first);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(Command(version.Id), TestContext.Current.CancellationToken);

        added.Value.Version.ShouldBe(7);
        version.Chapters.Select(chapter => chapter.Id.Value).ShouldBe([first.Value, added.Value]);
        var chapter = version.Chapters[1];
        chapter.Kind.ShouldBe(ChapterKind.Text);
        chapter.Title.ShouldBe(ChapterTitle.From("Flächendesinfektion"));
        chapter.Text.ShouldBe(MarkdownText.From("# Wischen\n\nNicht sprühen."));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();
        var unknown = CourseVersionId.New();

        var added = await Handler(store).HandleAsync(Command(unknown), TestContext.Current.CancellationToken);

        added.Error.Code.ShouldBe("editorial.course-version-not-found");
        added.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_published_version_gets_no_chapter_and_nothing_is_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(Command(version.Id), TestContext.Current.CancellationToken);

        added.Error.Code.ShouldBe("editorial.not-a-draft");
        added.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(chapter);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_in_review_gets_no_chapter()
    {
        var store = new InMemoryCourseVersionStore();
        var version = InReview();
        store.Stored.Add(version);

        var added = await Handler(store).HandleAsync(Command(version.Id), TestContext.Current.CancellationToken);

        added.Error.Code.ShouldBe("editorial.not-a-draft");
        added.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Commits.ShouldBe(0);
    }

    private static AddTextChapterHandler Handler(InMemoryCourseVersionStore store) => new(store, store);

    private static AddTextChapter Command(CourseVersionId version) =>
        new(version, ChapterTitle.From("Flächendesinfektion"), MarkdownText.From("# Wischen\n\nNicht sprühen."));
}
