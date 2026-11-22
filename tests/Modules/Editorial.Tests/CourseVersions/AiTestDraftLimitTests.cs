using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An AI test draft keeps the approved questions and replaces the rest; the approved ones and the draft together hold at
/// most <see cref="QuestionCount.Maximum"/> questions. A draft beyond it is refused as a rule of the version, never
/// thrown, and leaves the questions as they were.
/// </summary>
public class AiTestDraftLimitTests
{
    [Fact]
    public void A_draft_that_with_the_approved_questions_exceeds_the_maximum_is_refused_and_changes_nothing()
    {
        var version = WithApprovedAndUnreviewed(out var chapter);
        var before = Snapshot(version);

        var applied = version.ApplyAiTestDraft(Drafts(chapter, QuestionCount.Maximum), QuestionId.New, AnswerOptionId.New);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("editorial.too-many-questions");
        Snapshot(version).ShouldBe(before);
    }

    [Fact]
    public void A_draft_longer_than_the_maximum_on_its_own_is_refused_without_throwing_and_changes_nothing()
    {
        var version = WithApprovedAndUnreviewed(out var chapter);
        var before = Snapshot(version);

        var applied = Should.NotThrow(() => version.ApplyAiTestDraft(Drafts(chapter, QuestionCount.Maximum + 1), QuestionId.New, AnswerOptionId.New));

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe("editorial.too-many-questions");
        Snapshot(version).ShouldBe(before);
    }

    [Fact]
    public void A_draft_that_with_the_approved_questions_reaches_the_maximum_is_applied()
    {
        var version = WithApprovedAndUnreviewed(out var chapter);

        var applied = version.ApplyAiTestDraft(Drafts(chapter, QuestionCount.Maximum - 1), QuestionId.New, AnswerOptionId.New);

        applied.IsSuccess.ShouldBeTrue();
        version.Questions.Count.ShouldBe(QuestionCount.Maximum);
        version.Questions.Count(question => question.Review == ReviewState.Approved).ShouldBe(1);
    }

    /// <summary>A draft with one chapter, one approved question and one unreviewed question about it.</summary>
    private static CourseVersion WithApprovedAndUnreviewed(out ChapterId chapter)
    {
        var version = DraftWithApprovedQuestion(out chapter, out _);
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();
        return version;
    }

    private static DraftedQuestion[] Drafts(ChapterId chapter, int count) => [.. Enumerable.Range(0, count).Select(_ => SingleChoice(chapter))];

    private static List<(QuestionId Id, ReviewState Review, Provenance Provenance)> Snapshot(CourseVersion version) =>
        [.. version.Questions.Select(question => (question.Id, question.Review, question.Provenance))];
}
