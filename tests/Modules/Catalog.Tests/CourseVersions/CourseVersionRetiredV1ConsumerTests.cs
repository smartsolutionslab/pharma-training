using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.GetCourseVersion;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.CourseVersions;

public class CourseVersionRetiredV1ConsumerTests
{
    [Fact]
    public async Task A_retirement_is_stored_once_the_unit_of_work_commits()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Hygiene"), TestContext.Current.CancellationToken);

        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);

        var retirement = store.Retirements.ShouldHaveSingleItem();
        retirement.CourseVersionId.ShouldBe(courseVersionId);
        retirement.CourseId.ShouldBe(courseId);
        store.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_retirement_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionRetiredV1Consumer(store, store);
        var retired = CourseVersionEvents.Retired(Guid.CreateVersion7(), Guid.CreateVersion7());

        await consumer.HandleAsync(retired, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(retired, TestContext.Current.CancellationToken);

        store.Retirements.ShouldHaveSingleItem().CourseVersionId.ShouldBe(retired.CourseVersionId);
        store.Commits.ShouldBe(1);
    }

    /// <summary>
    /// Editorial may announce a retirement again under a new event identifier (#189), so the consumer deduplicates on
    /// the course version, not on the event.
    /// </summary>
    [Fact]
    public async Task A_retirement_already_stored_is_left_alone_whatever_the_identifier_of_the_event_announcing_it()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionRetiredV1Consumer(store, store);
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        var first = CourseVersionEvents.Retired(courseId, courseVersionId);
        var again = CourseVersionEvents.Retired(courseId, courseVersionId);
        again.Metadata.EventIdentifier.ShouldNotBe(first.Metadata.EventIdentifier);

        await consumer.HandleAsync(first, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(again, TestContext.Current.CancellationToken);

        store.Retirements.ShouldHaveSingleItem().CourseVersionId.ShouldBe(courseVersionId);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_is_kept_and_the_version_reads_as_retired_once_it_arrives()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);
        store.Retirements.ShouldHaveSingleItem().CourseVersionId.ShouldBe(courseVersionId);
        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Hygiene"), TestContext.Current.CancellationToken);

        var version = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(courseVersionId), TestContext.Current.CancellationToken);
        version.Value.IsRetired.ShouldBeTrue();
        version.Value.Title.ShouldBe("Hygiene");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task An_empty_course_or_version_id_is_refused_and_nothing_stored(bool emptyCourse, bool emptyVersion)
    {
        var store = new InMemoryCourseVersionStore();
        var retired = CourseVersionEvents.Retired(emptyCourse ? Guid.Empty : Guid.CreateVersion7(), emptyVersion ? Guid.Empty : Guid.CreateVersion7());

        await Should.ThrowAsync<GuardException>(() => new CourseVersionRetiredV1Consumer(store, store).HandleAsync(retired, TestContext.Current.CancellationToken));

        store.Retirements.ShouldBeEmpty();
    }
}
