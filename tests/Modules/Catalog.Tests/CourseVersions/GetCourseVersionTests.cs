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
        version.Status.ShouldBe(CatalogCourseVersionStatus.Current);
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
    public async Task A_version_without_a_newer_version_of_its_course_reads_as_current()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        await PublishAsync(store, courseId, v1, 1);

        var read = await ReadAsync(store, v1);

        read.Status.ShouldBe(CatalogCourseVersionStatus.Current);
        read.IsRetired.ShouldBeFalse();
    }

    [Fact]
    public async Task A_version_reads_as_superseded_once_a_newer_version_of_its_course_is_stored()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        await PublishAsync(store, courseId, v1, 1);
        await PublishAsync(store, courseId, v2, 2);

        var older = await ReadAsync(store, v1);
        var newer = await ReadAsync(store, v2);

        older.Status.ShouldBe(CatalogCourseVersionStatus.Superseded);
        older.IsRetired.ShouldBeFalse();
        newer.Status.ShouldBe(CatalogCourseVersionStatus.Current);
    }

    [Fact]
    public async Task An_older_version_stored_after_the_newer_reads_as_superseded()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        await PublishAsync(store, courseId, v2, 2);
        await PublishAsync(store, courseId, v1, 1);

        (await ReadAsync(store, v1)).Status.ShouldBe(CatalogCourseVersionStatus.Superseded);
        (await ReadAsync(store, v2)).Status.ShouldBe(CatalogCourseVersionStatus.Current);
    }

    [Fact]
    public async Task A_newer_version_of_another_course_supersedes_nothing()
    {
        var store = new InMemoryCourseVersionStore();
        var v1 = Guid.CreateVersion7();
        await PublishAsync(store, Guid.CreateVersion7(), v1, 1);
        await PublishAsync(store, Guid.CreateVersion7(), Guid.CreateVersion7(), 2);

        (await ReadAsync(store, v1)).Status.ShouldBe(CatalogCourseVersionStatus.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retired_version_reads_as_retired_whether_or_not_it_is_superseded(bool superseded)
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        await PublishAsync(store, courseId, v1, 1);
        if (superseded)
        {
            await PublishAsync(store, courseId, Guid.CreateVersion7(), 2);
        }

        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, v1), TestContext.Current.CancellationToken);

        var read = await ReadAsync(store, v1);
        read.Status.ShouldBe(CatalogCourseVersionStatus.Retired);
        read.IsRetired.ShouldBeTrue();
    }

    [Fact]
    public async Task A_superseded_version_stays_superseded_when_the_newer_version_is_retired()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        await PublishAsync(store, courseId, v1, 1);
        await PublishAsync(store, courseId, v2, 2);
        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, v2), TestContext.Current.CancellationToken);

        var older = await ReadAsync(store, v1);
        var newer = await ReadAsync(store, v2);

        older.Status.ShouldBe(CatalogCourseVersionStatus.Superseded);
        older.IsRetired.ShouldBeFalse();
        newer.Status.ShouldBe(CatalogCourseVersionStatus.Retired);
        newer.IsRetired.ShouldBeTrue();
    }

    [Fact]
    public void A_status_travels_by_name()
    {
        var json = JsonSerializer.Serialize(CatalogCourseVersionStatus.Superseded);

        json.ShouldBe("\"Superseded\"");
    }

    [Fact]
    public void A_chapter_kind_travels_by_name()
    {
        var json = JsonSerializer.Serialize(CatalogChapterKind.Video);

        json.ShouldBe("\"Video\"");
    }

    private static Task PublishAsync(InMemoryCourseVersionStore store, Guid courseId, Guid courseVersionId, int versionNumber) =>
        new CourseVersionPublishedV1Consumer(store, store).HandleAsync(
            CourseVersionEvents.Published(courseId, courseVersionId, versionNumber, $"Hygiene {versionNumber}"),
            TestContext.Current.CancellationToken);

    private static async Task<CatalogCourseVersionDto> ReadAsync(InMemoryCourseVersionStore store, Guid courseVersionId)
    {
        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(courseVersionId), TestContext.Current.CancellationToken);
        return read.Value;
    }
}
