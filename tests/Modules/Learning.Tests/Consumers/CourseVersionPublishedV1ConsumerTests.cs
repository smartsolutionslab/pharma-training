using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

public class CourseVersionPublishedV1ConsumerTests
{
    [Fact]
    public async Task A_published_version_is_stored_with_its_course_number_and_chapters_once_the_unit_of_work_commits()
    {
        var copies = new InMemoryCourseVersionCopies();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        var text = CourseVersionEvents.Text(1);
        var pdf = CourseVersionEvents.Pdf(2);
        var video = CourseVersionEvents.Video(3, 420);

        await new CourseVersionPublishedV1Consumer(copies, copies).HandleAsync(
            CourseVersionEvents.Published(courseId, courseVersionId, 3, [video, text, pdf]),
            TestContext.Current.CancellationToken);

        var copy = copies.Versions.ShouldHaveSingleItem();
        copy.Id.ShouldBe(courseVersionId);
        copy.CourseId.ShouldBe(courseId);
        copy.VersionNumber.ShouldBe(3);
        copy.Chapters
            .OrderBy(chapter => chapter.Position)
            .Select(chapter => (chapter.CourseVersionId, chapter.ChapterId, chapter.Position, chapter.Kind, chapter.VideoDurationSeconds))
            .ShouldBe(
            [
                (courseVersionId, text.ChapterId, 1, ChapterKind.Text, (int?)null),
                (courseVersionId, pdf.ChapterId, 2, ChapterKind.Pdf, (int?)null),
                (courseVersionId, video.ChapterId, 3, ChapterKind.Video, (int?)420),
            ]);
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var copies = new InMemoryCourseVersionCopies();
        var consumer = new CourseVersionPublishedV1Consumer(copies, copies);
        var first = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, [CourseVersionEvents.Text(1)]);

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(
            CourseVersionEvents.Published(first.CourseId, first.CourseVersionId, 1, [CourseVersionEvents.Pdf(1), CourseVersionEvents.Video(2, 60)]),
            TestContext.Current.CancellationToken);

        var copy = copies.Versions.ShouldHaveSingleItem();
        copy.Chapters.ShouldHaveSingleItem().ChapterId.ShouldBe(first.Chapters[0].ChapterId);
        copies.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData("empty course id", nameof(CourseVersionPublishedV1.CourseId))]
    [InlineData("empty course version id", nameof(CourseVersionPublishedV1.CourseVersionId))]
    [InlineData("null chapters", nameof(CourseVersionPublishedV1.Chapters))]
    [InlineData("no chapters", nameof(CourseVersionPublishedV1.Chapters))]
    [InlineData("an empty chapter id", nameof(CourseChapterV1.ChapterId))]
    [InlineData("a duplicate chapter id", nameof(CourseChapterV1.ChapterId))]
    [InlineData("position 0", nameof(CourseChapterV1.Position))]
    public async Task A_malformed_version_is_refused_by_name(string malformation, string field)
    {
        var copies = new InMemoryCourseVersionCopies();

        var refused = await Should.ThrowAsync<GuardException>(() =>
            new CourseVersionPublishedV1Consumer(copies, copies).HandleAsync(Malformed(malformation), TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.Versions.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    private static CourseVersionPublishedV1 Malformed(string malformation)
    {
        var text = CourseVersionEvents.Text(1);
        var wellFormed = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, [text, CourseVersionEvents.Video(2, 60)]);
        return malformation switch
        {
            "empty course id" => wellFormed with { CourseId = Guid.Empty },
            "empty course version id" => wellFormed with { CourseVersionId = Guid.Empty },
            "null chapters" => wellFormed with { Chapters = null! },
            "no chapters" => wellFormed with { Chapters = [] },
            "an empty chapter id" => wellFormed with { Chapters = [text with { ChapterId = Guid.Empty }] },
            "a duplicate chapter id" => wellFormed with { Chapters = [text, CourseVersionEvents.Pdf(2) with { ChapterId = text.ChapterId }] },
            "position 0" => wellFormed with { Chapters = [text with { Position = 0 }] },
            _ => throw new InvalidOperationException($"{malformation} is no malformation this test knows."),
        };
    }
}
