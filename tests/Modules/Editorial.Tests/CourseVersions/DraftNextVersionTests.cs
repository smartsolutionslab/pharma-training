using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.DraftNextVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor drafts the next version of a published one (E7): a draft of the same course numbered one higher, a copy
/// of its content with new ids whose approved questions stay approved. One successor per version: a second is refused
/// while the first exists.
/// </summary>
public class DraftNextVersionTests
{
    private static readonly PersonId Editor = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task The_next_version_of_a_published_one_is_stored_as_a_draft_numbered_one_higher_once_committed_and_its_id_answered()
    {
        var store = new InMemoryCourseVersionStore();
        var source = Published(out var chapter);
        store.Stored.Add(source);

        var drafted = await Handler(store, store).HandleAsync(new DraftNextVersion(source.Id, Editor), TestContext.Current.CancellationToken);

        drafted.IsSuccess.ShouldBeTrue();
        store.Stored.Count.ShouldBe(2);
        var next = store.Stored.Single(version => version.Id != source.Id);
        next.Id.Value.ShouldBe(drafted.Value);
        next.Course.ShouldBe(source.Course);
        next.Number.ShouldBe(VersionNumber.From(2));
        next.Status.ShouldBe(CourseVersionStatus.Draft);
        next.CreatedBy.ShouldBe(Editor);
        next.Title.ShouldBe(source.Title);
        next.Blueprint.ShouldBe(source.Blueprint);
        var copiedChapter = next.Chapters.ShouldHaveSingleItem();
        copiedChapter.Id.ShouldNotBe(chapter);
        var copiedQuestion = next.Questions.ShouldHaveSingleItem();
        copiedQuestion.Id.ShouldNotBe(source.Questions.Single().Id);
        copiedQuestion.Chapter.ShouldBe(copiedChapter.Id);
        copiedQuestion.Review.ShouldBe(ReviewState.Approved);
        store.Commits.ShouldBe(1);
        source.Status.ShouldBe(CourseVersionStatus.Published);
    }

    [Fact]
    public async Task An_unknown_version_is_not_found()
    {
        var store = new InMemoryCourseVersionStore();

        var drafted = await Handler(store, store).HandleAsync(new DraftNextVersion(CourseVersionId.New(), Editor), TestContext.Current.CancellationToken);

        drafted.IsFailure.ShouldBeTrue();
        drafted.Error.Code.ShouldBe("editorial.course-version-not-found");
        drafted.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    /// <summary>Retired is left to the integration tests: <c>Retire</c>'s signature changes in #177.</summary>
    [Theory]
    [InlineData("Draft")]
    [InlineData("InReview")]
    [InlineData("Superseded")]
    public async Task A_version_that_is_not_published_has_no_next_one(string status)
    {
        var store = new InMemoryCourseVersionStore();
        var source = InState(status);
        store.Stored.Add(source);

        var drafted = await Handler(store, store).HandleAsync(new DraftNextVersion(source.Id, Editor), TestContext.Current.CancellationToken);

        drafted.IsFailure.ShouldBeTrue();
        drafted.Error.Code.ShouldBe("editorial.not-published");
        drafted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        store.Stored.Count.ShouldBe(1);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_second_next_version_of_one_version_is_refused_while_the_first_exists()
    {
        var store = new InMemoryCourseVersionStore();
        var source = Published();
        var existing = source.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        store.Stored.Add(source);
        store.Stored.Add(existing);

        var drafted = await Handler(store, store).HandleAsync(new DraftNextVersion(source.Id, Editor), TestContext.Current.CancellationToken);

        drafted.IsFailure.ShouldBeTrue();
        drafted.Error.Code.ShouldBe("editorial.next-version-exists");
        drafted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        drafted.Error.Message.ShouldContain(existing.Id.Value.ToString());
        store.Stored.Count.ShouldBe(2);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_next_version_that_lost_the_race_is_told_the_version_changed()
    {
        var store = new InMemoryCourseVersionStore();
        var source = Published();
        store.Stored.Add(source);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var drafted = await Handler(store, lostRace).HandleAsync(new DraftNextVersion(source.Id, Editor), TestContext.Current.CancellationToken);

        drafted.IsFailure.ShouldBeTrue();
        drafted.Error.Code.ShouldBe("editorial.course-version-changed");
        drafted.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_next_version_is_saved_with_TryCommitAsync()
    {
        var store = new InMemoryCourseVersionStore();
        var source = Published();
        store.Stored.Add(source);
        var unitOfWork = Substitute.For<IEditorialUnitOfWork>();
        unitOfWork.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(true);

        var drafted = await Handler(store, unitOfWork).HandleAsync(new DraftNextVersion(source.Id, Editor), TestContext.Current.CancellationToken);

        drafted.IsSuccess.ShouldBeTrue();
        await unitOfWork.Received(1).TryCommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    private static DraftNextVersionHandler Handler(InMemoryCourseVersionStore store, IEditorialUnitOfWork unitOfWork) => new(store, unitOfWork);

    private static CourseVersion InState(string status)
    {
        switch (status)
        {
            case "Draft":
                return Draft();
            case "InReview":
                return InReview();
            case "Superseded":
                var superseded = Published();
                superseded.Supersede();
                superseded.Status.ShouldBe(CourseVersionStatus.Superseded);
                return superseded;
            default:
                throw new InvalidOperationException($"No version in state {status}.");
        }
    }
}
