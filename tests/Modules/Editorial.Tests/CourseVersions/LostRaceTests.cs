using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RemoveChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReorderChapters;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceTextChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A change whose version another save changed since it was loaded is refused as a conflict, not a fault.</summary>
public class LostRaceTests
{
    private readonly InMemoryCourseVersionStore store = new();
    private readonly IEditorialUnitOfWork lostRace = Substitute.For<IEditorialUnitOfWork>();
    private readonly CourseVersion version;
    private readonly ChapterId chapter;

    public LostRaceTests()
    {
        version = DraftWithChapter(out chapter);
        store.Stored.Add(version);
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);
    }

    [Fact]
    public async Task An_added_chapter_that_lost_the_race_is_told_the_version_changed()
    {
        var added = await new AddTextChapterHandler(store, lostRace)
            .HandleAsync(new AddTextChapter(version.Id, ChapterTitle.From("Neu"), MarkdownText.From("Neu.")), TestContext.Current.CancellationToken);

        ShouldBeChanged(added.Error);
    }

    [Fact]
    public async Task A_replaced_chapter_that_lost_the_race_is_told_the_version_changed()
    {
        var replaced = await new ReplaceTextChapterHandler(store, lostRace)
            .HandleAsync(new ReplaceTextChapter(version.Id, chapter, ChapterTitle.From("Neu"), MarkdownText.From("Neu.")), TestContext.Current.CancellationToken);

        ShouldBeChanged(replaced.Error);
    }

    [Fact]
    public async Task An_added_file_chapter_that_lost_the_race_is_told_the_version_changed()
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var added = await new AddFileChapterHandler(store, lostRace, blobs, new FakePdfTextExtractor())
            .HandleAsync(new AddFileChapter(version.Id, ChapterTitle.From("Neu"), ChapterKind.Pdf, BlobReference.From(file), null), TestContext.Current.CancellationToken);

        added.IsFailure.ShouldBeTrue();
        ShouldBeChanged(added.Error);
    }

    [Fact]
    public async Task A_replaced_file_chapter_that_lost_the_race_is_told_the_version_changed()
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(version.Id, ".mp4");
        blobs.Upload(file, "video/mp4", 1024);

        var replaced = await new ReplaceFileChapterHandler(store, lostRace, blobs, new FakePdfTextExtractor())
            .HandleAsync(new ReplaceFileChapter(version.Id, chapter, ChapterTitle.From("Neu"), ChapterKind.Video, BlobReference.From(file), VideoDuration.From(90)), TestContext.Current.CancellationToken);

        replaced.IsFailure.ShouldBeTrue();
        ShouldBeChanged(replaced.Error);
    }

    [Fact]
    public async Task A_removed_chapter_that_lost_the_race_is_told_the_version_changed()
    {
        var removed = await new RemoveChapterHandler(store, lostRace)
            .HandleAsync(new RemoveChapter(version.Id, chapter), TestContext.Current.CancellationToken);

        ShouldBeChanged(removed.Error);
    }

    [Fact]
    public async Task An_order_that_lost_the_race_is_told_the_version_changed()
    {
        var reordered = await new ReorderChaptersHandler(store, lostRace)
            .HandleAsync(new ReorderChapters(version.Id, [chapter]), TestContext.Current.CancellationToken);

        ShouldBeChanged(reordered.Error);
    }

    [Fact]
    public async Task A_unit_of_work_that_cannot_lose_a_race_commits_and_says_so()
    {
        IEditorialUnitOfWork unitOfWork = store;

        (await unitOfWork.TryCommitAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        store.Commits.ShouldBe(1);
    }

    private void ShouldBeChanged(ApiError error)
    {
        error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        error.Code.ShouldBe("editorial.course-version-changed");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
    }
}
