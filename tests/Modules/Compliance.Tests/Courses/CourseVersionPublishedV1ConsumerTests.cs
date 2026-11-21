using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;

public class CourseVersionPublishedV1ConsumerTests
{
    [Fact]
    public async Task A_published_version_is_stored_once_the_unit_of_work_commits()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 3, "Kühlkette in der Apotheke", 12), TestContext.Current.CancellationToken);

        var version = store.Versions.ShouldHaveSingleItem();
        version.Id.ShouldBe(CourseVersionId.From(courseVersionId));
        version.Course.ShouldBe(CourseId.From(courseId));
        version.Number.ShouldBe(VersionNumber.From(3));
        version.Title.ShouldBe(CourseTitle.From("Kühlkette in der Apotheke"));
        version.Validity.ShouldBe(ValidityMonths.From(12));
        store.Retirements.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_delivered_twice_is_stored_once_as_it_first_arrived_and_nothing_more_is_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var first = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Erste Fassung");

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(first.CourseId, first.CourseVersionId, 1, "Zweite Fassung", 36), TestContext.Current.CancellationToken);

        var version = store.Versions.ShouldHaveSingleItem();
        version.Title.ShouldBe(CourseTitle.From("Erste Fassung"));
        version.Validity.ShouldBe(ValidityMonths.From(24));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Two_versions_of_one_course_arriving_newer_first_are_both_stored_and_the_newer_is_current()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var courseId = Guid.CreateVersion7();
        var newer = Guid.CreateVersion7();

        await consumer.HandleAsync(CourseVersionEvents.Published(courseId, newer, 2, "Hygiene 2"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(courseId, Guid.CreateVersion7(), 1, "Hygiene 1"), TestContext.Current.CancellationToken);

        store.Versions.Select(version => version.Number.Value).Order().ShouldBe([1, 2]);
        var course = (await store.FindCourseAsync(CourseId.From(courseId), TestContext.Current.CancellationToken)).Value;
        course.CurrentVersion().Value.Id.ShouldBe(CourseVersionId.From(newer));
        course.Title.ShouldBe(CourseTitle.From("Hygiene 2"));
    }

    [Fact]
    public async Task A_version_without_chapters_is_stored_since_compliance_keeps_none()
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Hygiene") with { Chapters = null! };

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
            1,
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
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, title!);

        await RefusedAsync(store, published, nameof(CourseVersionPublishedV1.Title));
    }

    [Fact]
    public async Task A_version_number_below_one_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, "Hygiene");

        await RefusedAsync(store, published, nameof(CourseVersionPublishedV1.VersionNumber));
    }

    [Fact]
    public async Task A_validity_below_one_month_is_refused_by_its_name_and_nothing_stored()
    {
        var store = new InMemoryCourseVersionStore();
        var published = CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Hygiene", 0);

        await RefusedAsync(store, published, nameof(CourseVersionPublishedV1.ValidityMonths));
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
