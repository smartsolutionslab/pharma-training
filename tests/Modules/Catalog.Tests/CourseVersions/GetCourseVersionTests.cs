using System.Net;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.GetCourseVersion;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.CourseVersions;

public class GetCourseVersionTests
{
    [Fact]
    public async Task An_unknown_id_and_the_empty_guid_are_not_found()
    {
        var handler = new GetCourseVersionHandler(new InMemoryCourseVersionStore());
        var id = Guid.CreateVersion7();

        var unknown = await handler.HandleAsync(new GetCourseVersion(id), TestContext.Current.CancellationToken);
        var empty = await handler.HandleAsync(new GetCourseVersion(Guid.Empty), TestContext.Current.CancellationToken);

        unknown.Error.Code.ShouldBe("catalog.course-version-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Error.Message.ShouldBe($"There is no course version {id}.");
        empty.Error.Code.ShouldBe("catalog.course-version-not-found");
        empty.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_stored_version_is_read_with_its_chapters_in_reading_order_and_without_their_files()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        var published = CourseVersionEvents.Published(courseId, courseVersionId, 2, "Kühlkette in der Apotheke");
        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(courseVersionId), TestContext.Current.CancellationToken);

        var version = read.Value;
        version.CourseId.ShouldBe(courseId);
        version.CourseVersionId.ShouldBe(courseVersionId);
        version.VersionNumber.ShouldBe(2);
        version.Title.ShouldBe("Kühlkette in der Apotheke");
        version.ValidityMonths.ShouldBe(24);
        version.IsRetired.ShouldBeFalse();
        var text = published.Chapters.Single(chapter => chapter.Position == 1);
        var video = published.Chapters.Single(chapter => chapter.Position == 2);
        var pdf = published.Chapters.Single(chapter => chapter.Position == 3);
        version.Chapters.ShouldBe(
        [
            new CatalogChapterDto(text.ChapterId, 1, "Grundlagen", CatalogChapterKind.Text, null, "# Grundlagen\n\nKühlkette **immer** einhalten."),
            new CatalogChapterDto(video.ChapterId, 2, "Lagerung", CatalogChapterKind.Video, 420, null),
            new CatalogChapterDto(pdf.ChapterId, 3, "Checkliste", CatalogChapterKind.Pdf, null, null),
        ]);
        var json = JsonSerializer.Serialize(version, JsonSerializerOptions.Web);
        json.ShouldNotContain("\"file\"");
        json.ShouldNotContain("blobs/");
    }

    [Fact]
    public async Task A_retired_version_is_still_read_and_says_it_is_retired()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Hygiene"), TestContext.Current.CancellationToken);
        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(courseVersionId), TestContext.Current.CancellationToken);

        read.Value.CourseVersionId.ShouldBe(courseVersionId);
        read.Value.IsRetired.ShouldBeTrue();
    }

    [Fact]
    public void A_chapter_kind_travels_by_name()
    {
        var json = JsonSerializer.Serialize(CatalogChapterKind.Video);

        json.ShouldBe("\"Video\"");
    }
}
