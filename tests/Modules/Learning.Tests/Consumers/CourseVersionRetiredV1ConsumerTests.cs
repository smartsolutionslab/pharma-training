using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

public class CourseVersionRetiredV1ConsumerTests
{
    [Fact]
    public async Task A_retired_version_is_stored_as_a_retirement_once_the_unit_of_work_commits()
    {
        var copies = new InMemoryCourseVersionCopies();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();
        await new CourseVersionPublishedV1Consumer(copies, copies).HandleAsync(
            CourseVersionEvents.Published(courseId, courseVersionId, 1, [CourseVersionEvents.Text(1)]),
            TestContext.Current.CancellationToken);

        await new CourseVersionRetiredV1Consumer(copies, copies).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);

        var retirement = copies.Retirements.ShouldHaveSingleItem();
        retirement.CourseVersionId.ShouldBe(courseVersionId);
        retirement.CourseId.ShouldBe(courseId);
        (await copies.IsRetiredAsync(CourseVersionId.From(courseVersionId), TestContext.Current.CancellationToken)).ShouldBeTrue();
        copies.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task A_retirement_delivered_twice_is_stored_once_and_nothing_more_is_committed()
    {
        var copies = new InMemoryCourseVersionCopies();
        var consumer = new CourseVersionRetiredV1Consumer(copies, copies);
        var retired = CourseVersionEvents.Retired(Guid.CreateVersion7(), Guid.CreateVersion7());

        await consumer.HandleAsync(retired, TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Retired(retired.CourseId, retired.CourseVersionId), TestContext.Current.CancellationToken);

        copies.Retirements.ShouldHaveSingleItem();
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_is_stored_without_it()
    {
        var copies = new InMemoryCourseVersionCopies();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionRetiredV1Consumer(copies, copies).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);

        var retirement = copies.Retirements.ShouldHaveSingleItem();
        retirement.CourseVersionId.ShouldBe(courseVersionId);
        retirement.CourseId.ShouldBe(courseId);
        copies.Versions.ShouldBeEmpty();
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_retirement_and_then_its_version_leave_both_stored()
    {
        var copies = new InMemoryCourseVersionCopies();
        var courseId = Guid.CreateVersion7();
        var courseVersionId = Guid.CreateVersion7();

        await new CourseVersionRetiredV1Consumer(copies, copies).HandleAsync(CourseVersionEvents.Retired(courseId, courseVersionId), TestContext.Current.CancellationToken);
        await new CourseVersionPublishedV1Consumer(copies, copies).HandleAsync(
            CourseVersionEvents.Published(courseId, courseVersionId, 1, [CourseVersionEvents.Text(1)]),
            TestContext.Current.CancellationToken);

        copies.Versions.ShouldHaveSingleItem().Id.ShouldBe(courseVersionId);
        copies.Retirements.ShouldHaveSingleItem().CourseVersionId.ShouldBe(courseVersionId);
        (await copies.IsRetiredAsync(CourseVersionId.From(courseVersionId), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(nameof(CourseVersionRetiredV1.CourseId))]
    [InlineData(nameof(CourseVersionRetiredV1.CourseVersionId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_stored(string field)
    {
        var copies = new InMemoryCourseVersionCopies();
        var retired = CourseVersionEvents.Retired(
            field == nameof(CourseVersionRetiredV1.CourseId) ? Guid.Empty : Guid.CreateVersion7(),
            field == nameof(CourseVersionRetiredV1.CourseVersionId) ? Guid.Empty : Guid.CreateVersion7());

        var refused = await Should.ThrowAsync<GuardException>(() => new CourseVersionRetiredV1Consumer(copies, copies).HandleAsync(retired, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.Retirements.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_stored()
    {
        var copies = new InMemoryCourseVersionCopies();

        await Should.ThrowAsync<GuardException>(() => new CourseVersionRetiredV1Consumer(copies, copies).HandleAsync(null!, TestContext.Current.CancellationToken));

        copies.Retirements.ShouldBeEmpty();
        copies.Commits.ShouldBe(0);
    }
}
