using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>An editor signs a question off: who and when are recorded (story E7), and only a well-formed question can be.</summary>
public class ApproveQuestionTests
{
    private static readonly DateTimeOffset Start = new(2027, 3, 2, 14, 30, 0, TimeSpan.Zero);

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task An_approved_question_of_a_draft_records_its_approver_and_the_time_once_committed()
    {
        var version = DraftWithQuestion(out _, out var id);
        store.Stored.Add(version);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, id, Reviewer), TestContext.Current.CancellationToken);

        approved.IsSuccess.ShouldBeTrue();
        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Approved);
        question.ReviewedBy.ShouldBe(Reviewer);
        question.ReviewedAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(1);
    }

    /// <summary>The first approval stands: a second one is a conflict with the question's state, and nothing is saved.</summary>
    [Fact]
    public async Task Approving_an_approved_question_again_answers_conflict_and_the_first_approval_stands()
    {
        var version = DraftWithQuestion(out _, out var id);
        store.Stored.Add(version);
        (await Handler().HandleAsync(new ApproveQuestion(version.Id, id, Author), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        time.Advance(TimeSpan.FromHours(3));

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, id, Reviewer), TestContext.Current.CancellationToken);

        approved.IsFailure.ShouldBeTrue("an approved question was approved again");
        approved.Error.Code.ShouldBe("editorial.question-already-approved");
        approved.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        var question = version.Questions.ShouldHaveSingleItem();
        question.ReviewedBy.ShouldBe(Author);
        question.ReviewedAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_question_of_a_version_in_review_can_be_approved()
    {
        var version = DraftWithQuestion(out _, out _);
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly)).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        var question = version.Questions.ShouldHaveSingleItem();
        store.Stored.Add(version);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, question.Id, Reviewer), TestContext.Current.CancellationToken);

        approved.IsSuccess.ShouldBeTrue();
        question.ReviewedBy.ShouldBe(Reviewer);
        question.ReviewedAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_single_choice_question_with_two_correct_answers_cannot_be_processed()
    {
        var (version, id) = DraftHolding(TwoCorrectSingleChoice);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, id, Reviewer), TestContext.Current.CancellationToken);

        ShouldBeUnprocessable(approved.Error, "editorial.single-choice-one-correct", version);
    }

    [Fact]
    public async Task A_multiple_choice_question_without_a_correct_answer_cannot_be_processed()
    {
        var (version, id) = DraftHolding(NoCorrectMultipleChoice);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, id, Reviewer), TestContext.Current.CancellationToken);

        ShouldBeUnprocessable(approved.Error, "editorial.multiple-choice-a-correct", version);
    }

    [Fact]
    public async Task A_question_with_a_single_option_cannot_be_processed()
    {
        var (version, id) = DraftHolding(OneOption);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, id, Reviewer), TestContext.Current.CancellationToken);

        ShouldBeUnprocessable(approved.Error, "editorial.too-few-options", version);
    }

    [Fact]
    public async Task A_question_of_a_published_version_is_not_approved_again()
    {
        var version = Published();
        var question = version.Questions.ShouldHaveSingleItem();
        store.Stored.Add(version);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, question.Id, Reviewer), TestContext.Current.CancellationToken);

        approved.Error.Code.ShouldBe("editorial.not-a-draft");
        approved.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        question.ReviewedBy.ShouldBe(Author);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var approved = await Handler().HandleAsync(new ApproveQuestion(CourseVersionId.New(), QuestionId.New(), Reviewer), TestContext.Current.CancellationToken);

        approved.Error.Code.ShouldBe("editorial.course-version-not-found");
        approved.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_the_version_does_not_hold_is_not_found()
    {
        var version = DraftWithQuestion(out _, out _);
        store.Stored.Add(version);

        var approved = await Handler().HandleAsync(new ApproveQuestion(version.Id, QuestionId.New(), Reviewer), TestContext.Current.CancellationToken);

        approved.Error.Code.ShouldBe("editorial.question-not-found");
        approved.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    private ApproveQuestionHandler Handler() => new(store, store, time);

    private (CourseVersion Version, QuestionId Question) DraftHolding(Func<ChapterId, DraftedQuestion> draft)
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(QuestionId.New(), draft(chapter), AnswerOptionId.New).Value;
        store.Stored.Add(version);
        return (version, question);
    }

    private void ShouldBeUnprocessable(ApiError error, string code, CourseVersion version)
    {
        error.Code.ShouldBe(code);
        error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var question = version.Questions.ShouldHaveSingleItem();
        question.Review.ShouldBe(ReviewState.Unreviewed);
        question.ReviewedBy.ShouldBeNull();
        store.Commits.ShouldBe(0);
    }
}
