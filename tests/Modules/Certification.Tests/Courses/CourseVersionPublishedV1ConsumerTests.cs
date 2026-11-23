using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Courses;

/// <summary>Certification's copy of a published course version: its course, title and validity; stored once.</summary>
public class CourseVersionPublishedV1ConsumerTests
{
    [Fact]
    public async Task A_published_version_is_stored_with_its_course_title_and_validity_once_the_unit_of_work_commits()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, "Kühlkette in der Apotheke", 24), TestContext.Current.CancellationToken);

        var version = store.Versions.ShouldHaveSingleItem();
        version.Id.ShouldBe(CourseVersionId.From(courseVersionId));
        version.Course.ShouldBe(CourseId.From(courseId));
        version.Title.ShouldBe(CourseTitle.From("Kühlkette in der Apotheke"));
        version.Validity.ShouldBe(RecurrenceInterval.From(24));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var first = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), "Erste Fassung");

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(first.CourseId, first.CourseVersionId, "Zweite Fassung", 36), TestContext.Current.CancellationToken);

        var version = store.Versions.ShouldHaveSingleItem();
        version.Title.ShouldBe(CourseTitle.From("Erste Fassung"));
        version.Validity.ShouldBe(RecurrenceInterval.From(12));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_without_chapters_is_stored_since_certification_keeps_none()
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), "Hygiene") with { Chapters = null! };

        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken);

        store.Versions.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(nameof(CourseVersionPublishedV1.CourseId))]
    [InlineData(nameof(CourseVersionPublishedV1.CourseVersionId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(
            field == nameof(CourseVersionPublishedV1.CourseId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(CourseVersionPublishedV1.CourseVersionId) ? Guid.Empty : Guid.CreateVersion7(),
            "Hygiene");

        await RefusedAsync(store, published, field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Kühl\0kette")]
    public async Task A_missing_or_blank_title_or_one_holding_a_nul_character_is_refused_by_its_name_and_nothing_stored(string? title)
    {
        var store = new InMemoryCourseVersionStore();

        await RefusedAsync(store, CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), title!), nameof(CourseVersionPublishedV1.Title));
    }

    [Fact]
    public async Task A_title_as_long_as_editorial_allows_is_stored()
    {
        var store = new InMemoryCourseVersionStore();
        var title = new string('a', 150);

        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), title), TestContext.Current.CancellationToken);

        store.Versions.ShouldHaveSingleItem().Title.ShouldBe(CourseTitle.From(title));
    }

    [Fact]
    public async Task A_title_longer_than_editorial_allows_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryCourseVersionStore();

        await RefusedAsync(store, CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 151)), nameof(CourseVersionPublishedV1.Title));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task A_validity_outside_one_to_sixty_months_is_refused_by_its_name_and_nothing_stored(int months)
    {
        var store = new InMemoryCourseVersionStore();

        await RefusedAsync(store, CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), "Hygiene", months), nameof(CourseVersionPublishedV1.ValidityMonths));
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryCourseVersionStore();

        await Should.ThrowAsync<GuardException>(() => new CourseVersionPublishedV1Consumer(store, store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Versions.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static async Task RefusedAsync(InMemoryCourseVersionStore store, CourseVersionPublishedV1 published, string field)
    {
        var refused = await Should.ThrowAsync<GuardException>(() => new CourseVersionPublishedV1Consumer(store, store).HandleAsync(published, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Versions.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
