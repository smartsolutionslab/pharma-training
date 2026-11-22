using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A version in review goes back to its editors as a draft, and takes changes again.</summary>
public class ReturnToDraftTests
{
    private readonly InMemoryCourseVersionStore store = new();

    [Fact]
    public async Task A_version_in_review_returns_to_draft()
    {
        var version = InReview();
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_draft_cannot_be_returned()
    {
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var returned = await Handler().HandleAsync(new ReturnToDraft(version.Id), TestContext.Current.CancellationToken);

        returned.Error.Code.ShouldBe("editorial.not-in-review");
        returned.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var returned = await Handler().HandleAsync(new ReturnToDraft(CourseVersionId.New()), TestContext.Current.CancellationToken);

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

        var returned = await new ReturnToDraftHandler(store, lostRace).HandleAsync(new ReturnToDraft(version.Id), TestContext.Current.CancellationToken);

        returned.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        returned.Error.Code.ShouldBe("editorial.course-version-changed");
        returned.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private ReturnToDraftHandler Handler() => new(store, store);
}
