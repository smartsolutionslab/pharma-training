using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A version in review goes back to its editors as a draft, and takes changes again; the record of who returned it,
/// when and why is stored in the same commit as the version's change (#181).
/// </summary>
public class ReturnToDraftTests
{
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 11, 20, 0, TimeSpan.Zero);
    private static readonly ReturnReason Reason = ReturnReason.From("Kapitel 2 braucht eine Quelle.");
    private static readonly ReturnReason OtherReason = ReturnReason.From("Frage 1 ist mehrdeutig.");

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task A_version_in_review_returns_to_draft()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_records_the_caller_and_the_time_and_raises_returned()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Approver, ApproversName, Reason), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        version.ReturnedBy.ShouldBe(Approver);
        version.ReturnedAt.ShouldBe(Timestamp.From(Start));
        version.DomainEvents.OfType<CourseVersionReturned>().ShouldHaveSingleItem().ShouldBe(new CourseVersionReturned(version.Id, Approver, Reason));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_stores_its_record_in_the_same_commit()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Approver, ApproversName, Reason), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        var record = store.Returns.ShouldHaveSingleItem();
        record.Version.ShouldBe(version.Id);
        record.ReturnedBy.ShouldBe(Approver);
        record.ReturnedAt.ShouldBe(Timestamp.From(Start));
        record.Reason.ShouldBe(Reason);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_stores_the_returners_name_with_its_record()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        var record = store.Returns.ShouldHaveSingleItem();
        record.ReturnedBy.ShouldBe(Reviewer);
        record.ReturnedByName.ShouldBe(ReviewersName);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_return_adds_a_record_and_replaces_none()
    {
        var version = InReview();
        store.Stored.Add(version);
        (await Handler().HandleAsync(new ReturnToDraft(version.Id, Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        time.Advance(TimeSpan.FromHours(1));

        (await Handler().HandleAsync(new ReturnToDraft(version.Id, Approver, ApproversName, OtherReason), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        store.Returns.Count.ShouldBe(2);
        store.Returns[0].ReturnedBy.ShouldBe(Reviewer);
        store.Returns[0].ReturnedByName.ShouldBe(ReviewersName);
        store.Returns[0].Reason.ShouldBe(Reason);
        store.Returns[0].ReturnedAt.ShouldBe(Timestamp.From(Start));
        store.Returns[1].ReturnedBy.ShouldBe(Approver);
        store.Returns[1].ReturnedByName.ShouldBe(ApproversName);
        store.Returns[1].Reason.ShouldBe(OtherReason);
        store.Returns[1].ReturnedAt.ShouldBe(Timestamp.From(Start.AddHours(1)));
        store.Returns[1].Id.ShouldNotBe(store.Returns[0].Id);
    }

    [Fact]
    public async Task A_draft_cannot_be_returned()
    {
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id, Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken);

        returned.Error.Code.ShouldBe("editorial.not-in-review");
        returned.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(0);
        store.Returns.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var returned = await Handler().HandleAsync(new ReturnToDraft(CourseVersionId.New(), Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken);

        returned.Error.Code.ShouldBe("editorial.course-version-not-found");
        returned.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
        store.Returns.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_return_that_lost_a_race_answers_changed()
    {
        var version = InReview();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var returned = await new ReturnToDraftHandler(store, lostRace, time).HandleAsync(new ReturnToDraft(version.Id, Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken);

        returned.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        returned.Error.Code.ShouldBe("editorial.course-version-changed");
        returned.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Returns.ShouldBeEmpty();
    }

    private ReturnToDraftHandler Handler() => new(store, store, time);
}
