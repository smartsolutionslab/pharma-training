using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A question as the endpoints answer it: its content, where it came from, and who signed it off when (story E7).</summary>
public class QuestionMappingsTests
{
    public static TheoryData<QuestionType> Types => Every<QuestionType>();

    public static TheoryData<Provenance> Provenances => Every<Provenance>();

    public static TheoryData<ReviewState> Reviews => Every<ReviewState>();

    [Fact]
    public void An_approved_question_carries_its_content_its_provenance_its_approver_and_the_time()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out var id);
        var question = version.Questions.ShouldHaveSingleItem();

        var dto = question.ToDto(1);

        dto.Id.ShouldBe(id.Value);
        dto.Position.ShouldBe(1);
        dto.ChapterId.ShouldBe(chapter.Value);
        dto.Text.ShouldBe("Wann desinfizieren Sie die Hände?");
        dto.Type.ShouldBe(EditorialQuestionType.SingleChoice);
        dto.Options.ShouldBe(
        [
            new EditorialAnswerOptionDto(question.Options[0].Id.Value, "Vor jedem Kundenkontakt mit Rezeptur", true),
            new EditorialAnswerOptionDto(question.Options[1].Id.Value, "Einmal am Tag", false),
        ]);
        dto.Provenance.ShouldBe(EditorialQuestionProvenance.Human);
        dto.Review.ShouldBe(EditorialQuestionReview.Approved);
        dto.ReviewedBy.ShouldBe(Author.Value);
        dto.ReviewedAt.ShouldBe(Now.Value);
    }

    [Fact]
    public void An_unreviewed_question_has_no_approver_and_no_time()
    {
        var version = DraftWithQuestion(out _, out _);

        var dto = version.Questions.ShouldHaveSingleItem().ToDto(3);

        dto.Position.ShouldBe(3);
        dto.Review.ShouldBe(EditorialQuestionReview.Unreviewed);
        dto.ReviewedBy.ShouldBeNull();
        dto.ReviewedAt.ShouldBeNull();
    }

    /// <summary>The name is the one the token carried at approval time; the approval time is the review time (#205).</summary>
    [Fact]
    public void An_approved_question_carries_the_reviewers_name_and_the_time_of_its_approval()
    {
        var version = DraftWithApprovedQuestion(out _, out _);

        var dto = version.Questions.ShouldHaveSingleItem().ToDto(1);

        dto.ApprovedByName.ShouldBe(AuthorsName.Value);
        dto.ApprovedAt.ShouldBe(Now.Value);
        dto.ApprovedAt.ShouldBe(dto.ReviewedAt);
    }

    [Fact]
    public void An_unreviewed_question_has_no_reviewers_name_and_no_approval_time()
    {
        var version = DraftWithQuestion(out _, out _);

        var dto = version.Questions.ShouldHaveSingleItem().ToDto(1);

        dto.ApprovedByName.ShouldBeNull();
        dto.ApprovedAt.ShouldBeNull();
    }

    [Fact]
    public void An_ai_drafted_question_says_so()
    {
        var version = DraftWithChapter(out var chapter);
        version.ApplyAiTestDraft([MultipleChoice(chapter)], QuestionId.New, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        var dto = version.Questions.ShouldHaveSingleItem().ToDto(1);

        dto.Provenance.ShouldBe(EditorialQuestionProvenance.AiDrafted);
        dto.Type.ShouldBe(EditorialQuestionType.MultipleChoice);
        dto.Options.Select(option => (option.Text, option.Correct)).ShouldBe([("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false)]);
    }

    [Fact]
    public void An_answer_option_carries_its_id_its_text_and_whether_it_is_correct()
    {
        var version = DraftWithQuestion(out _, out _);
        var option = version.Questions.ShouldHaveSingleItem().Options[1];

        option.ToDto().ShouldBe(new EditorialAnswerOptionDto(option.Id.Value, "Einmal am Tag", false));
    }

    [Fact]
    public void A_version_lists_its_questions_in_their_order_counted_from_one()
    {
        var version = DraftWithQuestion(out var chapter, out var first);
        var second = version.AddQuestion(QuestionId.New(), MultipleChoice(chapter), AnswerOptionId.New).Value;

        var dto = version.ToDto();

        dto.Questions.Select(question => (question.Id, question.Position)).ShouldBe([(first.Value, 1), (second.Value, 2)]);
    }

    [Fact]
    public void A_version_without_questions_lists_none()
    {
        var version = DraftWithChapter(out _);

        version.ToDto().Questions.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void A_question_type_travels_under_its_own_name_to_the_dto_and_back(QuestionType type)
    {
        var dto = type.ToDto();

        dto.ToString().ShouldBe(type.ToString());
        dto.ToDomain().ShouldBe(type);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void A_question_type_travels_under_its_own_name_in_the_integration_events(QuestionType type) =>
        type.ToV1().ToString().ShouldBe(type.ToString());

    [Fact]
    public void An_undefined_question_type_is_a_bug_in_the_integration_events() =>
        Should.Throw<UnreachableException>(() => ((QuestionType)99).ToV1());

    [Theory]
    [MemberData(nameof(Provenances))]
    public void A_provenance_travels_under_its_own_name_to_the_dto(Provenance provenance) =>
        provenance.ToDto().ToString().ShouldBe(provenance.ToString());

    [Theory]
    [MemberData(nameof(Reviews))]
    public void A_review_state_travels_under_its_own_name_to_the_dto(ReviewState review) =>
        review.ToDto().ToString().ShouldBe(review.ToString());

    [Fact]
    public void A_question_type_a_request_leaves_out_is_no_type()
    {
        Enum.IsDefined(default(EditorialQuestionType)).ShouldBeFalse();
        Enum.IsDefined((EditorialQuestionType)7).ShouldBeFalse();
    }

    [Fact]
    public void An_undefined_type_provenance_or_review_is_a_bug()
    {
        Should.Throw<UnreachableException>(() => ((QuestionType)99).ToDto());
        Should.Throw<UnreachableException>(() => default(EditorialQuestionType).ToDomain());
        Should.Throw<UnreachableException>(() => ((Provenance)99).ToDto());
        Should.Throw<UnreachableException>(() => ((ReviewState)99).ToDto());
    }

    private static TheoryData<TEnum> Every<TEnum>()
        where TEnum : struct, Enum
    {
        var data = new TheoryData<TEnum>();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            data.Add(value);
        }

        return data;
    }
}
