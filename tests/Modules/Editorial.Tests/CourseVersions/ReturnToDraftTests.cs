using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A version in review goes back to its editors as a draft, and takes changes again.</summary>
public class ReturnToDraftTests
{
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 11, 20, 0, TimeSpan.Zero);

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task A_version_in_review_returns_to_draft()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Reviewer), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_records_the_caller_and_the_time_and_raises_returned()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Approver), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        version.ReturnedBy.ShouldBe(Approver);
        version.ReturnedAt.ShouldBe(Timestamp.From(Start));
        version.DomainEvents.OfType<CourseVersionReturned>().ShouldHaveSingleItem().ShouldBe(new CourseVersionReturned(version.Id, Approver));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_draft_cannot_be_returned()
    {
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Reviewer), TestContext.Current.CancellationToken);

        returned.Error.Code.ShouldBe("editorial.not-in-review");
        returned.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var returned = await Handler().HandleAsync(new ReturnToDraft(CourseVersionId.New(), Reviewer), TestContext.Current.CancellationToken);

        returned.Error.Code.ShouldBe("editorial.course-version-not-found");
        returned.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_return_that_lost_a_race_answers_changed()
    {
        var version = InReview();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var returned = await new ReturnToDraftHandler(store, lostRace, time).HandleAsync(new ReturnToDraft(version.Id, Reviewer), TestContext.Current.CancellationToken);

        returned.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        returned.Error.Code.ShouldBe("editorial.course-version-changed");
        returned.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private ReturnToDraftHandler Handler() => new(store, store, time);
}
