using System.Net;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor retires a published or superseded version, and the module announces it to Catalog and Compliance with
/// <see cref="CourseVersionRetiredV1"/>, sent only with a commit that succeeds.
/// </summary>
public class RetireCourseVersionTests
{
    private readonly InMemoryCourseVersionStore store = new();

    [Fact]
    public async Task A_published_version_is_retired_and_announced()
    {
        var version = Published();
        store.Stored.Add(version);

        var retired = await Handler().HandleAsync(new RetireCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        retired.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Retired);
        ShouldBeAnnounced(version);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_superseded_version_is_retired_too()
    {
        var version = Published();
        version.Supersede();
        store.Stored.Add(version);

        var retired = await Handler().HandleAsync(new RetireCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        retired.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Retired);
        ShouldBeAnnounced(version);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_draft_is_not_retired()
    {
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var retired = await Handler().HandleAsync(new RetireCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        retired.Error.Code.ShouldBe("editorial.not-published");
        retired.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var retired = await Handler().HandleAsync(new RetireCourseVersion(CourseVersionId.New(), Reviewer), TestContext.Current.CancellationToken);

        retired.Error.Code.ShouldBe("editorial.course-version-not-found");
        retired.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    /// <summary>
    /// The event goes to the unit of work, never around it, so the commit that lost the race takes it with it (ADR
    /// 0002: a lost race sends nothing): nothing reaches the committed events.
    /// </summary>
    [Fact]
    public async Task A_retire_that_lost_a_race_announces_nothing()
    {
        var version = Published();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var retired = await new RetireCourseVersionHandler(store, lostRace).HandleAsync(new RetireCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        retired.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        retired.Error.Code.ShouldBe("editorial.course-version-changed");
        retired.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        await lostRace.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private RetireCourseVersionHandler Handler() => new(store, store);

    private void ShouldBeAnnounced(CourseVersion version)
    {
        var announced = store.Published.ShouldHaveSingleItem().ShouldBeOfType<CourseVersionRetiredV1>();
        announced.CourseId.ShouldBe(version.Course.Value);
        announced.CourseVersionId.ShouldBe(version.Id.Value);
    }
}
