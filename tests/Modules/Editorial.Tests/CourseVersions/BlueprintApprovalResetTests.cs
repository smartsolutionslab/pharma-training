using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Changing a draft's test settings takes every question's approval away (the owner's decision of 2026-11-23, #199):
/// whichever of the number of questions, the passing score or the validity changes, every question of the version,
/// copied or not, needs a review again, so the changed test is signed off before it is published. No event is raised.
/// </summary>
public class BlueprintApprovalResetTests
{
    [Fact]
    public void Changing_the_number_of_questions_takes_every_questions_approval_away()
    {
        ChangingTakesEveryApprovalAway(Blueprint(11, 80, 12));
    }

    [Fact]
    public void Changing_the_passing_score_takes_every_questions_approval_away()
    {
        ChangingTakesEveryApprovalAway(Blueprint(10, 81, 12));
    }

    [Fact]
    public void Changing_the_validity_takes_every_questions_approval_away()
    {
        ChangingTakesEveryApprovalAway(Blueprint(10, 80, 13));
    }

    [Fact]
    public void Changing_a_setting_resets_unreviewed_and_ai_drafted_questions_alike_and_keeps_every_question()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out var human);
        var drafted = version.ApplyAiTestDraft([MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New).Value.ShouldHaveSingleItem();
        Approve(version, drafted, Approver, ApproversName);
        var unreviewed = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        var before = Contents(version);
        version.Questions.Single(question => question.Id == drafted).Provenance.ShouldBe(Provenance.AiDrafted);
        version.Questions.Select(question => question.Review).ShouldBe([ReviewState.Approved, ReviewState.Approved, ReviewState.Unreviewed]);

        var set = version.SetBlueprint(Blueprint(10, 81, 12));

        set.IsSuccess.ShouldBeTrue();
        version.Questions.Select(question => question.Id).ShouldBe([human, drafted, unreviewed]);
        Contents(version).ShouldBe(before);
        ShouldAllBeUnreviewed(version);
    }

    [Fact]
    public void Changing_a_setting_raises_no_domain_event()
    {
        var version = DraftWithTwoApprovedQuestions();
        IReadOnlyList<object> eventsBefore = [.. version.DomainEvents];

        var set = version.SetBlueprint(Blueprint(10, 81, 12));

        set.IsSuccess.ShouldBeTrue();
        ShouldAllBeUnreviewed(version);
        version.DomainEvents.Count.ShouldBe(eventsBefore.Count);
        for (var index = 0; index < eventsBefore.Count; index++)
        {
            version.DomainEvents[index].ShouldBeSameAs(eventsBefore[index]);
        }
    }

    [Fact]
    public void Changing_a_setting_of_the_next_version_takes_the_copied_questions_approval_away()
    {
        var next = Published().DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        next.Questions.ShouldNotBeEmpty();
        next.Questions.ShouldAllBe(question => question.Review == ReviewState.Approved);

        var set = next.SetBlueprint(Blueprint(1, 81, 12));

        set.IsSuccess.ShouldBeTrue();
        ShouldAllBeUnreviewed(next);
    }

    [Fact]
    public void A_draft_whose_settings_changed_after_approval_is_submitted_but_not_published_until_its_questions_are_approved_again()
    {
        var version = ReadyToSubmit();
        var question = version.Questions.ShouldHaveSingleItem().Id;
        version.SetBlueprint(Blueprint(1, 90, 12)).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();

        var refused = version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now);

        refused.IsFailure.ShouldBeTrue("a version whose settings changed after its questions were approved was published unreviewed");
        refused.Error.Value.ShouldBeOfType<QuestionNotApproved>().Code.ShouldBe("editorial.question-not-approved");
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        Approve(version, question, Reviewer, ReviewersName);
        version.Publish(Reviewer, ReviewPolicy.SingleEditor, Now).IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
    }

    [Fact]
    public void Saving_the_same_settings_again_keeps_every_approval_and_raises_no_event()
    {
        var version = DraftWithTwoApprovedQuestions();
        var approvals = Approvals(version);
        IReadOnlyList<object> eventsBefore = [.. version.DomainEvents];

        // A new instance with equal values, so value equality is what is tested, not reference equality.
        var same = Blueprint(10, 80, 12);
        same.ShouldNotBeSameAs(version.Blueprint);
        var set = version.SetBlueprint(same);

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(TestBlueprint.Default);
        Approvals(version).ShouldBe(approvals);
        version.Questions.ShouldAllBe(question => question.Review == ReviewState.Approved);
        version.DomainEvents.Count.ShouldBe(eventsBefore.Count);
        for (var index = 0; index < eventsBefore.Count; index++)
        {
            version.DomainEvents[index].ShouldBeSameAs(eventsBefore[index]);
        }
    }

    /// <summary>A regression pin of the check order, green before and after #199: a version that is no draft refuses first.</summary>
    [Fact]
    public void Saving_the_same_settings_on_a_version_in_review_is_still_refused_as_not_a_draft()
    {
        var version = InReview();
        var approvals = Approvals(version);
        var same = new TestBlueprint(
            QuestionCount.From(version.Blueprint.Questions.Value),
            ScorePercent.From(version.Blueprint.PassingScore.Value),
            RecurrenceInterval.From(version.Blueprint.Validity.Value));

        var set = version.SetBlueprint(same);

        set.IsFailure.ShouldBeTrue();
        set.Error.ShouldBeOfType<NotADraft>().Code.ShouldBe("editorial.not-a-draft");
        Approvals(version).ShouldBe(approvals);
        version.Questions.ShouldAllBe(question => question.Review == ReviewState.Approved);
    }

    private static IReadOnlyList<(QuestionId Id, ReviewState Review, PersonId? ReviewedBy, Timestamp? ReviewedAt)> Approvals(CourseVersion version) =>
        [.. version.Questions.Select(question => (question.Id, question.Review, question.ReviewedBy, question.ReviewedAt))];

    private static void ChangingTakesEveryApprovalAway(TestBlueprint changed)
    {
        var version = DraftWithTwoApprovedQuestions();
        version.Blueprint.ShouldBe(TestBlueprint.Default);

        var set = version.SetBlueprint(changed);

        set.IsSuccess.ShouldBeTrue();
        version.Blueprint.ShouldBe(changed);
        version.Questions.Count.ShouldBe(2);
        ShouldAllBeUnreviewed(version);
    }

    private static void ShouldAllBeUnreviewed(CourseVersion version)
    {
        foreach (var question in version.Questions)
        {
            question.Review.ShouldBe(ReviewState.Unreviewed, $"question {question.Id} kept its approval");
            question.ReviewedBy.ShouldBeNull();
            question.ReviewedAt.ShouldBeNull();
        }
    }

    /// <summary>A draft under the default blueprint with one chapter and two questions, the first approved by the author, the second by the approver.</summary>
    private static CourseVersion DraftWithTwoApprovedQuestions()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out _);
        var second = version.AddQuestion(QuestionId.New(), MultipleChoice(chapter), AnswerOptionId.New).Value;
        Approve(version, second, Approver, ApproversName);
        return version;
    }

    /// <summary>The one place this class approves directly, where no builder gives the approver it needs.</summary>
    private static void Approve(CourseVersion version, QuestionId question, PersonId editor, ReviewerName name) =>
        version.ApproveQuestion(question, editor, name, Now).IsSuccess.ShouldBeTrue();

    private static IReadOnlyList<string> Contents(CourseVersion version) =>
        [.. version.Questions.Select(question =>
            $"{question.Id}|{question.Text}|{question.Type}|{question.Chapter}|{question.Provenance}|"
            + string.Join(";", question.Options.Select(option => $"{option.Id}:{option.Text}:{option.Correctness}")))];

    private static TestBlueprint Blueprint(int questions, int passingScore, int validityMonths) =>
        new(QuestionCount.From(questions), ScorePercent.From(passingScore), RecurrenceInterval.From(validityMonths));
}
