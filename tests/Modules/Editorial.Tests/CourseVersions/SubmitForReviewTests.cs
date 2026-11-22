using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SubmitForReview;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>An editor submits a complete draft for review: it needs chapters and as many questions as its blueprint asks.</summary>
public class SubmitForReviewTests
{
    private readonly InMemoryCourseVersionStore store = new();

    [Fact]
    public async Task A_complete_draft_is_submitted_by_the_caller()
    {
        var version = ReadyToSubmit();
        store.Stored.Add(version);

        var submitted = await Handler().HandleAsync(new SubmitForReview(version.Id, Reviewer), TestContext.Current.CancellationToken);

        submitted.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.SubmittedBy.ShouldBe(Reviewer);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var submitted = await Handler().HandleAsync(new SubmitForReview(CourseVersionId.New(), Author), TestContext.Current.CancellationToken);

        submitted.Error.Code.ShouldBe("editorial.course-version-not-found");
        submitted.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_draft_without_chapters_is_not_submitted()
    {
        var version = Draft();
        store.Stored.Add(version);

        var submitted = await Handler().HandleAsync(new SubmitForReview(version.Id, Author), TestContext.Current.CancellationToken);

        ShouldBeRefused(submitted.Error, "editorial.no-chapters", version);
    }

    [Fact]
    public async Task A_draft_whose_test_is_incomplete_is_not_submitted()
    {
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);

        var submitted = await Handler().HandleAsync(new SubmitForReview(version.Id, Author), TestContext.Current.CancellationToken);

        ShouldBeRefused(submitted.Error, "editorial.test-incomplete", version);
    }

    [Fact]
    public async Task A_version_in_review_is_not_submitted_again()
    {
        var version = InReview();
        store.Stored.Add(version);

        var submitted = await Handler().HandleAsync(new SubmitForReview(version.Id, Reviewer), TestContext.Current.CancellationToken);

        submitted.Error.Code.ShouldBe("editorial.not-a-draft");
        submitted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.SubmittedBy.ShouldBe(Author);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_submit_that_lost_a_race_answers_changed()
    {
        var version = ReadyToSubmit();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var submitted = await new SubmitForReviewHandler(store, lostRace).HandleAsync(new SubmitForReview(version.Id, Author), TestContext.Current.CancellationToken);

        submitted.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        submitted.Error.Code.ShouldBe("editorial.course-version-changed");
        submitted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private SubmitForReviewHandler Handler() => new(store, store);

    private void ShouldBeRefused(ApiError error, string code, CourseVersion version)
    {
        error.Code.ShouldBe(code);
        error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        version.SubmittedBy.ShouldBeNull();
        store.Commits.ShouldBe(0);
    }
}
