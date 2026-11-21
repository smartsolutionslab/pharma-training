using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.CourseVersions;

public class CourseVersionPublishedV1ConsumerTests
{
    [Fact]
    public async Task A_published_version_is_stored_with_its_chapters_once_the_unit_of_work_commits()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        var published = CourseVersionEvents.Published(courseId, courseVersionId, 3, "Kühlkette in der Apotheke");

        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken);

        var version = store.Stored.ShouldHaveSingleItem();
        version.Id.ShouldBe(courseVersionId);
        version.CourseId.ShouldBe(courseId);
        version.VersionNumber.ShouldBe(3);
        version.Title.ShouldBe("Kühlkette in der Apotheke");
        version.ValidityMonths.ShouldBe(24);
        version.Chapters.Count.ShouldBe(3);
        foreach (var sent in published.Chapters)
        {
            var chapter = version.Chapters.Single(stored => stored.ChapterId == sent.ChapterId);
            chapter.CourseVersionId.ShouldBe(courseVersionId);
            chapter.Position.ShouldBe(sent.Position);
            chapter.Title.ShouldBe(sent.Title);
            chapter.Kind.ToString().ShouldBe(sent.Kind.ToString());
            chapter.VideoDurationSeconds.ShouldBe(sent.VideoDurationSeconds);
            chapter.Text.ShouldBe(sent.Text);
            chapter.File.ShouldBe(sent.File);
        }

        store.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData(ChapterKindV1.Text, ChapterKind.Text)]
    [InlineData(ChapterKindV1.Pdf, ChapterKind.Pdf)]
    [InlineData(ChapterKindV1.Video, ChapterKind.Video)]
    public async Task Each_kind_of_chapter_is_stored_as_its_own_kind(ChapterKindV1 sent, ChapterKind stored)
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Hygiene", [new CourseChapterV1(Guid.CreateVersion7(), 1, "Kapitel", sent, null, null, null)]);

        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken);

        store.Stored.ShouldHaveSingleItem().Chapters.ShouldHaveSingleItem().Kind.ShouldBe(stored);
    }

    [Fact]
    public async Task A_version_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var first = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Erste Fassung");
        var again = first with { Title = "Zweite Fassung" };

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(again, TestContext.Current.CancellationToken);

        store.Stored.ShouldHaveSingleItem().Title.ShouldBe("Erste Fassung");
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_already_stored_is_left_alone_whatever_the_identifier_of_the_event_announcing_it()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await consumer.HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Erste Fassung"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Zweite Fassung"), TestContext.Current.CancellationToken);

        store.Stored.ShouldHaveSingleItem().Title.ShouldBe("Erste Fassung");
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Two_versions_of_one_course_are_both_stored()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var courseId = Guid.CreateVersion7();

        await consumer.HandleAsync(CourseVersionEvents.Published(courseId, Guid.CreateVersion7(), 2, "Hygiene 2"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(courseId, Guid.CreateVersion7(), 1, "Hygiene 1"), TestContext.Current.CancellationToken);

        store.Stored.Select(version => version.VersionNumber).Order().ShouldBe([1, 2]);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("chapter title")]
    [InlineData("text")]
    [InlineData("file")]
    public async Task A_string_holding_a_nul_character_is_refused_and_nothing_stored(string where)
    {
        var store = new InMemoryCourseVersionStore();
        var title = where == "title" ? "Kühl\0kette" : "Kühlkette";
        var chapter = new CourseChapterV1(
            Guid.CreateVersion7(),
            1,
            where == "chapter title" ? "Grund\0lagen" : "Grundlagen",
            ChapterKindV1.Text,
            null,
            where == "text" ? "# Grund\0lagen" : "# Grundlagen",
            where == "file" ? "blobs/\0.pdf" : null);
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, title, [chapter]);

        await Should.ThrowAsync<GuardException>(() => new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken));

        store.Stored.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task An_empty_course_or_version_id_is_refused_and_nothing_stored(bool emptyCourse, bool emptyVersion)
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(emptyCourse ? Guid.Empty : Guid.CreateVersion7(), emptyVersion ? Guid.Empty : Guid.CreateVersion7(), 1, "Hygiene");

        await Should.ThrowAsync<GuardException>(() => new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken));

        store.Stored.ShouldBeEmpty();
    }
}
