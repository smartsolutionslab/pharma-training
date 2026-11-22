using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor publishes a version in review once every question is approved; with four eyes, not the one who submitted
/// it. Publishing supersedes the course's published version in the same save, and only a publish that succeeds does.
/// </summary>
public class PublishCourseVersionTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 10, 15, 0, TimeSpan.Zero);

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task A_reviewed_version_is_published_by_the_caller_at_the_current_time()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
        version.PublishedAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Publishing_supersedes_the_published_version_of_the_same_course()
    {
        var first = Published();
        var next = NextInReview(first);
        store.Stored.AddRange([first, next]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(next.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        first.Status.ShouldBe(CourseVersionStatus.Superseded);
        next.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Publishing_leaves_other_courses_published_versions_alone()
    {
        var other = Published("Arzneimittelsicherheit 2027");
        var version = InReview();
        store.Stored.AddRange([other, version]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        other.Status.ShouldBe(CourseVersionStatus.Published);
        version.Status.ShouldBe(CourseVersionStatus.Published);
    }

    [Fact]
    public async Task With_four_eyes_on_the_submitter_cannot_publish()
    {
        var first = Published();
        var next = NextInReview(first);
        store.Stored.AddRange([first, next]);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(next.Id, Author), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.four-eyes");
        published.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        next.Status.ShouldBe(CourseVersionStatus.InReview);
        next.PublishedBy.ShouldBeNull();
        first.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(0);
    }

    /// <summary>The owner's decision of 2026-11-22: under four eyes no one who approved a question publishes; forbidden, like the submitter.</summary>
    [Fact]
    public async Task With_four_eyes_on_an_editor_who_approved_a_question_cannot_publish()
    {
        var version = InReviewWithASecondApprover(Approver);
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(version.Id, Approver), TestContext.Current.CancellationToken);

        published.IsFailure.ShouldBeTrue("an editor who approved one of the version's questions published it under four eyes");
        published.Error.Code.ShouldBe("editorial.publisher-approved-questions");
        published.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.PublishedBy.ShouldBeNull();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task With_four_eyes_on_another_editor_publishes()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task With_four_eyes_off_the_submitter_publishes()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.SingleEditor)).HandleAsync(new PublishCourseVersion(version.Id, Author), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Author);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_with_an_unapproved_question_is_not_published()
    {
        var first = Published();
        var next = first.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        var chapter = next.Chapters.ShouldHaveSingleItem().Id;
        next.ReplaceChapter(TextChapter(chapter, "Händehygiene", "Neu: vor und nach …")).IsSuccess.ShouldBeTrue();
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        store.Stored.AddRange([first, next]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(next.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.question-not-approved");
        published.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        next.Status.ShouldBe(CourseVersionStatus.InReview);
        first.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_draft_is_not_published()
    {
        var version = ReadyToSubmit();
        store.Stored.Add(version);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.not-in-review");
        published.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(CourseVersionId.New(), Reviewer), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.course-version-not-found");
        published.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_publish_that_lost_a_race_answers_changed()
    {
        var version = InReview();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var published = await new PublishCourseVersionHandler(store, lostRace, ReviewSettings.Default, time)
            .HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        published.Error.Code.ShouldBe("editorial.course-version-changed");
        published.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private PublishCourseVersionHandler Handler(ReviewSettings review) => new(store, store, review, time);
}
