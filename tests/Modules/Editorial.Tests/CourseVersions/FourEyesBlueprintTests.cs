using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The owner's decision of 2026-11-23 (#246): with four eyes on, the editor who last changed a version's test blueprint
/// does not publish it, as the submitter does not. <see cref="CourseVersion.BlueprintChangedBy"/> records that editor
/// on a change that reset at least one approval (the issue's "changed after its last approval"; orchestrator default,
/// 2026-11-24): a change with no approval to reset records no one. The last such change wins, a return and a
/// resubmission keep it, and the next version starts without one. The refusal is <see cref="FourEyesRequired"/>, checked
/// before <see cref="PublisherApprovedQuestions"/>. With four eyes off nothing changes.
/// </summary>
public class FourEyesBlueprintTests
{
    private const string FourEyes = "editorial.four-eyes";

    [Fact]
    public void With_four_eyes_on_the_editor_who_last_changed_the_test_blueprint_cannot_publish()
    {
        var version = InReviewWithBlueprintChangedBy(Reviewer);

        var published = version.Publish(Reviewer, ReviewPolicy.FourEyes, Now);

        published.IsFailure.ShouldBeTrue("the editor who last changed the test blueprint published the version under four eyes");
        published.Error.Value.ShouldBeOfType<FourEyesRequired>().Code.ShouldBe(FourEyes);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.PublishedBy.ShouldBeNull();
        version.DomainEvents.OfType<CourseVersionPublished>().ShouldBeEmpty();
    }

    /// <summary>Both rules apply to an editor who changed the blueprint and approved: the four-eyes code wins, as for the submitter.</summary>
    [Fact]
    public void With_four_eyes_on_a_blueprint_changer_who_also_approved_is_refused_with_the_four_eyes_code()
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(OneQuestion(80), Reviewer).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(question, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var published = version.Publish(Reviewer, ReviewPolicy.FourEyes, Now);

        published.IsFailure.ShouldBeTrue();
        published.Error.Value.ShouldBeOfType<FourEyesRequired>().Code.ShouldBe(FourEyes);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
    }

    [Fact]
    public void With_four_eyes_on_an_editor_who_neither_submitted_approved_nor_changed_the_blueprint_publishes()
    {
        var version = InReviewWithBlueprintChangedBy(Reviewer);
        version.BlueprintChangedBy.ShouldBe(Reviewer);

        var published = version.Publish(Approver, ReviewPolicy.FourEyes, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Approver);
    }

    /// <summary>The approver changes the blueprint first, the reviewer last: only the reviewer is refused.</summary>
    [Fact]
    public void With_four_eyes_on_only_the_last_blueprint_changer_is_refused()
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(OneQuestion(80), Approver).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(OneQuestion(81), Reviewer).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.BlueprintChangedBy.ShouldBe(Reviewer);

        var byLastChanger = version.Publish(Reviewer, ReviewPolicy.FourEyes, Now);
        byLastChanger.IsFailure.ShouldBeTrue("the last blueprint changer published the version under four eyes");
        byLastChanger.Error.Value.ShouldBeOfType<FourEyesRequired>();
        version.Status.ShouldBe(CourseVersionStatus.InReview);

        var byEarlierChanger = version.Publish(Approver, ReviewPolicy.FourEyes, Now);
        byEarlierChanger.IsSuccess.ShouldBeTrue();
        version.PublishedBy.ShouldBe(Approver);
    }

    [Fact]
    public void With_four_eyes_off_the_blueprint_changer_publishes()
    {
        var version = InReviewWithBlueprintChangedBy(Reviewer);
        version.BlueprintChangedBy.ShouldBe(Reviewer);

        var published = version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
    }

    [Fact]
    public void Changing_the_blueprint_with_no_approval_to_reset_records_no_changer()
    {
        var version = DraftWithChapter(out var chapter);
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        version.SetBlueprint(OneQuestion(80), Reviewer).IsSuccess.ShouldBeTrue();

        version.BlueprintChangedBy.ShouldBeNull();
    }

    [Fact]
    public void Saving_the_same_blueprint_again_records_no_changer()
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();

        version.SetBlueprint(TestBlueprint.Default, Reviewer).IsSuccess.ShouldBeTrue();
        version.BlueprintChangedBy.ShouldBeNull();

        version.SetBlueprint(OneQuestion(80), Approver).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(OneQuestion(80), Reviewer).IsSuccess.ShouldBeTrue();
        version.BlueprintChangedBy.ShouldBe(Approver);
    }

    [Fact]
    public void A_refused_blueprint_change_records_no_changer()
    {
        var version = InReviewWithBlueprintChangedBy(Approver);

        var refused = version.SetBlueprint(OneQuestion(90), Reviewer);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.ShouldBeOfType<NotADraft>();
        version.BlueprintChangedBy.ShouldBe(Approver);
    }

    [Fact]
    public void The_blueprint_changer_is_kept_across_a_return_and_a_resubmission()
    {
        var version = InReviewWithBlueprintChangedBy(Reviewer);
        version.ReturnToDraft(Approver, ApproversName, ReturnReason.From("Kapitel 1 braucht eine Quelle."), Now).IsSuccess.ShouldBeTrue();
        version.BlueprintChangedBy.ShouldBe(Reviewer);
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        version.BlueprintChangedBy.ShouldBe(Reviewer);

        var published = version.Publish(Reviewer, ReviewPolicy.FourEyes, Now);

        published.IsFailure.ShouldBeTrue("a return and a resubmission let the blueprint changer publish under four eyes");
        published.Error.Value.ShouldBeOfType<FourEyesRequired>();
        version.Status.ShouldBe(CourseVersionStatus.InReview);
    }

    [Fact]
    public void The_next_version_starts_without_a_blueprint_changer()
    {
        var published = InReviewWithBlueprintChangedBy(Reviewer);
        published.Publish(Approver, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        published.BlueprintChangedBy.ShouldBe(Reviewer);

        var next = published.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;

        next.BlueprintChangedBy.ShouldBeNull();
    }

    /// <summary>
    /// A version of one question whose blueprint the given editor changed after the author had approved the question,
    /// which reset that approval; the author approved it again and submitted the version.
    /// </summary>
    private static CourseVersion InReviewWithBlueprintChangedBy(PersonId changer)
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(OneQuestion(80), changer).IsSuccess.ShouldBeTrue();
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static TestBlueprint OneQuestion(int passingScore) =>
        new(QuestionCount.From(1), ScorePercent.From(passingScore), RecurrenceInterval.Yearly);
}
