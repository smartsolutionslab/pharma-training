using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class MappingsTests
{
    public static TheoryData<CourseVersionStatus> Statuses
    {
        get
        {
            var data = new TheoryData<CourseVersionStatus>();
            foreach (var status in Enum.GetValues<CourseVersionStatus>())
            {
                data.Add(status);
            }

            return data;
        }
    }

    public static TheoryData<ChapterKind> Kinds
    {
        get
        {
            var data = new TheoryData<ChapterKind>();
            foreach (var kind in Enum.GetValues<ChapterKind>())
            {
                data.Add(kind);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void A_status_travels_under_its_own_name_to_the_dto_and_back(CourseVersionStatus status)
    {
        var dto = status.ToDto();

        dto.ToString().ShouldBe(status.ToString());
        dto.ToDomain().ShouldBe(status);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_chapter_kind_travels_under_its_own_name_to_the_dto(ChapterKind kind) =>
        kind.ToDto().ToString().ShouldBe(kind.ToString());

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_chapter_kind_travels_under_its_own_name_in_the_integration_events(ChapterKind kind) =>
        kind.ToV1().ToString().ShouldBe(kind.ToString());

    [Fact]
    public void An_undefined_chapter_kind_is_a_bug_in_the_integration_events() =>
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((ChapterKind)99).ToV1());

    [Fact]
    public void An_undefined_status_or_kind_is_a_bug()
    {
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((CourseVersionStatus)99).ToDto());
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((EditorialCourseVersionStatus)99).ToDomain());
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((ChapterKind)99).ToDto());
    }

    [Fact]
    public void A_version_is_summed_up_without_its_chapters()
    {
        var version = Published(out _);

        version.ToSummaryDto().ShouldBe(new EditorialCourseVersionSummaryDto(version.Id.Value, version.Course.Value, 1, "Hygiene 2027", EditorialCourseVersionStatus.Published));
    }

    [Fact]
    public void A_chapter_carries_its_text_or_its_file_and_a_videos_duration_in_the_dto()
    {
        var version = DraftWithChapter(out var first);
        var pdf = ChapterId.New();
        var video = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From("courses/plan.pdf"))).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfVideo(video, ChapterTitle.From("Vorführung"), BlobReference.From("courses/demo.mp4"), VideoDuration.From(90))).IsSuccess.ShouldBeTrue();

        var dto = version.ToDto();

        dto.Chapters.ShouldBe(
        [
            new EditorialChapterDto(first.Value, 1, "Händehygiene", EditorialChapterKind.Text, "Hände vor und nach …", null, null),
            new EditorialChapterDto(pdf.Value, 2, "Hygieneplan", EditorialChapterKind.Pdf, null, "courses/plan.pdf", null),
            new EditorialChapterDto(video.Value, 3, "Vorführung", EditorialChapterKind.Video, null, "courses/demo.mp4", 90),
        ]);
    }

    /// <summary>Each count lands in its own field: the three differ, so a swap shows.</summary>
    [Fact]
    public void Republished_events_map_to_a_dto_with_their_three_counts()
    {
        var dto = new RepublishedEvents(5, 4, 2).ToDto();

        dto.ShouldBe(new EditorialRepublishedEventsDto(5, 4, 2));
        dto.CourseVersions.ShouldBe(5);
        dto.TestDefinitions.ShouldBe(4);
        dto.Retirements.ShouldBe(2);
    }
}
