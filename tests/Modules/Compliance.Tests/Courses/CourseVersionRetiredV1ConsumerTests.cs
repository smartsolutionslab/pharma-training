using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Courses;

public class CourseVersionRetiredV1ConsumerTests
{
    [Fact]
    public async Task A_retired_version_is_stored_as_a_retirement_once_the_unit_of_work_commits()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);

        var retirement = store.Retirements.ShouldHaveSingleItem();
        retirement.Id.ShouldBe(CourseVersionId.From(courseVersionId));
        retirement.Course.ShouldBe(CourseId.From(courseId));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_retirement_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionRetiredV1Consumer(store, store);
        var retired = CourseVersionEvents.Retired(Guid.CreateVersion7(), Guid.CreateVersion7());

        await consumer.HandleAsync(retired, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Retired(retired.CourseId, retired.CourseVersionId), TestContext.Current.CancellationToken);

        store.Retirements.ShouldHaveSingleItem();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Retiring_the_current_version_leaves_the_course_without_one()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Hygiene"), TestContext.Current.CancellationToken);

        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);

        store.Versions.ShouldHaveSingleItem();
        var course = (await store.FindCourseAsync(CourseId.From(courseId), TestContext.Current.CancellationToken)).Value;
        course.IsRetired.ShouldBeTrue();
        course.CurrentVersion().HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_is_kept_and_the_course_reads_as_retired_once_the_version_arrives()
    {
        var store = new InMemoryCourseVersionStore();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionRetiredV1Consumer(store, store).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);
        await new CourseVersionPublishedV1Consumer(store, store).HandleAsync(CourseVersionEvents.Published(courseId, courseVersionId, 1, "Hygiene"), TestContext.Current.CancellationToken);

        store.Retirements.ShouldHaveSingleItem();
        store.Versions.ShouldHaveSingleItem();
        var course = (await store.FindCourseAsync(CourseId.From(courseId), TestContext.Current.CancellationToken)).Value;
        course.IsRetired.ShouldBeTrue();
        course.CurrentVersion().HasValue.ShouldBeFalse();
    }

    [Theory]
    [InlineData(nameof(CourseVersionRetiredV1.CourseId))]
    [InlineData(nameof(CourseVersionRetiredV1.CourseVersionId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var store = new InMemoryCourseVersionStore();
        var retired = CourseVersionEvents.Retired(
            field == nameof(CourseVersionRetiredV1.CourseId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(CourseVersionRetiredV1.CourseVersionId) ? Guid.Empty : Guid.CreateVersion7());

        var refused = await Should.ThrowAsync<GuardException>(() => new CourseVersionRetiredV1Consumer(store, store).HandleAsync(retired, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        store.Retirements.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var store = new InMemoryCourseVersionStore();

        await Should.ThrowAsync<GuardException>(() => new CourseVersionRetiredV1Consumer(store, store).HandleAsync(null!, TestContext.Current.CancellationToken));

        store.Retirements.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }
}
