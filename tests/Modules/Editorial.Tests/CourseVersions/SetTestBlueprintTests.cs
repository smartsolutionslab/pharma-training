using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SetTestBlueprint;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor sets the test blueprint of a draft: it replaces the whole blueprint, removes no question but takes every
/// question's approval away when a setting changes (#199), and is refused for a version that is no draft or was changed
/// by another save since it was loaded.
/// </summary>
public class SetTestBlueprintTests
{
    private readonly InMemoryCourseVersionStore store = new();
    private readonly IEditorialUnitOfWork unitOfWork = Substitute.For<IEditorialUnitOfWork>();

    public SetTestBlueprintTests()
    {
        unitOfWork.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task A_drafts_blueprint_is_replaced_and_saved()
    {
        var version = Draft();
        store.Stored.Add(version);
        var blueprint = Blueprint(5, 70, 24);

        var set = await SetAsync(version.Id, blueprint);

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(blueprint);
        await unitOfWork.Received(1).TryCommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(11, 80, 12)]
    [InlineData(10, 81, 12)]
    [InlineData(10, 80, 13)]
    public async Task Approved_questions_lose_their_approval_when_one_setting_changes(int questions, int passingScore, int validityMonths)
    {
        var version = DraftWithApprovedQuestion(out _, out var question);
        store.Stored.Add(version);
        var blueprint = Blueprint(questions, passingScore, validityMonths);

        var set = await SetAsync(version.Id, blueprint);

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(blueprint);
        var reset = version.Questions.ShouldHaveSingleItem();
        reset.Id.ShouldBe(question);
        reset.Review.ShouldBe(ReviewState.Unreviewed);
        reset.ReviewedBy.ShouldBeNull();
        await unitOfWork.Received(1).TryCommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_the_same_blueprint_again_keeps_every_approval()
    {
        var version = DraftWithApprovedQuestion(out _, out var question);
        store.Stored.Add(version);

        var set = await SetAsync(version.Id, Blueprint(10, 80, 12));

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(TestBlueprint.Default);
        var kept = version.Questions.ShouldHaveSingleItem();
        kept.Id.ShouldBe(question);
        kept.Review.ShouldBe(ReviewState.Approved);
        kept.ReviewedBy.ShouldBe(Author);
    }

    [Fact]
    public async Task A_count_below_the_questions_held_removes_none()
    {
        var version = DraftWithQuestion(out var chapter, out var first);
        var second = version.AddQuestion(QuestionId.New(), MultipleChoice(chapter), AnswerOptionId.New).Value;
        store.Stored.Add(version);
        var blueprint = Blueprint(1, 80, 12);

        var set = await SetAsync(version.Id, blueprint);

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(blueprint);
        version.Questions.Select(question => question.Id).ShouldBe([first, second]);
    }

    [Fact]
    public async Task An_unknown_version_is_not_found()
    {
        var set = await SetAsync(CourseVersionId.New(), Blueprint(5, 70, 24));

        set.IsFailure.ShouldBeTrue();
        set.Error.Code.ShouldBe("editorial.course-version-not-found");
        set.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        await unitOfWork.DidNotReceive().TryCommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("InReview")]
    [InlineData("Published")]
    public async Task A_version_that_is_no_draft_refuses_and_saves_nothing(string status)
    {
        var version = status == "InReview" ? InReview() : Published();
        store.Stored.Add(version);
        var before = version.Blueprint;

        var set = await SetAsync(version.Id, Blueprint(5, 70, 24));

        set.IsFailure.ShouldBeTrue();
        set.Error.Code.ShouldBe("editorial.not-a-draft");
        set.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Blueprint.ShouldBe(before);
        await unitOfWork.DidNotReceive().TryCommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_blueprint_that_lost_the_race_is_told_the_version_changed()
    {
        var version = Draft();
        store.Stored.Add(version);
        unitOfWork.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var set = await SetAsync(version.Id, Blueprint(5, 70, 24));

        set.IsFailure.ShouldBeTrue();
        set.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        set.Error.Code.ShouldBe("editorial.course-version-changed");
        set.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private static TestBlueprint Blueprint(int questions, int passingScore, int validityMonths) =>
        new(QuestionCount.From(questions), ScorePercent.From(passingScore), RecurrenceInterval.From(validityMonths));

    private Task<Result<Unit, ApiError>> SetAsync(CourseVersionId version, TestBlueprint blueprint) =>
        new SetTestBlueprintHandler(store, unitOfWork).HandleAsync(new SetTestBlueprint(version, blueprint), TestContext.Current.CancellationToken);
}
