using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.GetCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class GetCourseVersionTests
{
    [Fact]
    public async Task A_version_is_read_with_its_chapters_in_order_counted_from_one()
    {
        var store = new InMemoryCourseVersionStore();
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        version.ReorderChapters([second, first]).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(version.Id.Value), TestContext.Current.CancellationToken);

        var dto = read.Value;
        dto.Id.ShouldBe(version.Id.Value);
        dto.CourseId.ShouldBe(version.Course.Value);
        dto.Number.ShouldBe(1);
        dto.Title.ShouldBe("Hygiene 2027");
        dto.Status.ShouldBe(EditorialCourseVersionStatus.Draft);
        dto.Chapters.ShouldBe(
        [
            new EditorialChapterDto(second.Value, 1, "Flächendesinfektion", EditorialChapterKind.Text, "Wischen."),
            new EditorialChapterDto(first.Value, 2, "Händehygiene", EditorialChapterKind.Text, "Hände vor und nach …"),
        ]);
    }

    [Fact]
    public async Task A_published_version_is_read_as_published()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published();
        store.Stored.Add(version);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(version.Id.Value), TestContext.Current.CancellationToken);

        read.Value.Status.ShouldBe(EditorialCourseVersionStatus.Published);
    }

    [Fact]
    public async Task An_unknown_id_and_the_empty_guid_are_not_found()
    {
        var handler = new GetCourseVersionHandler(new InMemoryCourseVersionStore());

        var unknown = await handler.HandleAsync(new GetCourseVersion(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        var empty = await handler.HandleAsync(new GetCourseVersion(Guid.Empty), TestContext.Current.CancellationToken);

        unknown.Error.Code.ShouldBe("editorial.course-version-not-found");
        unknown.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        empty.Error.Code.ShouldBe("editorial.course-version-not-found");
        empty.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
