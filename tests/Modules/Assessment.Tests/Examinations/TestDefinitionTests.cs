using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

public class TestDefinitionTests
{
    private static readonly ChapterId Hygiene = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Gloves = ChapterId.From(Guid.CreateVersion7());
    private static readonly AnswerOptionId A = NewOption();
    private static readonly AnswerOptionId B = NewOption();
    private static readonly AnswerOptionId C = NewOption();
    private static readonly AnswerOptionId D = NewOption();
    private static readonly QuestionVersionId Single = QuestionVersionId.From(Guid.CreateVersion7());
    private static readonly QuestionVersionId Multiple = QuestionVersionId.From(Guid.CreateVersion7());

    // Single choice (Hygiene): A right. Multiple choice (Gloves): B and C right.
    private static readonly TestDefinition Definition = new(
        CourseVersionId.From(Guid.CreateVersion7()),
        [
            new DefinedQuestion(Single, Hygiene, [A, D], [A]),
            new DefinedQuestion(Multiple, Gloves, [B, C, D], [B, C]),
        ],
        ScorePercent.From(80),
        AttemptNumber.From(3));

    [Fact]
    public void Exactly_the_correct_options_are_right()
    {
        Definition.Judge(Single, [A]).ShouldBe(AnswerVerdict.Right);
        Definition.Judge(Multiple, [B, C]).ShouldBe(AnswerVerdict.Right);
    }

    [Fact]
    public void The_order_of_the_chosen_options_does_not_matter() =>
        Definition.Judge(Multiple, [C, B]).ShouldBe(AnswerVerdict.Right);

    [Fact]
    public void A_subset_of_the_correct_options_is_wrong() =>
        Definition.Judge(Multiple, [B]).ShouldBe(AnswerVerdict.Wrong);

    [Fact]
    public void A_wrong_option_beside_the_correct_ones_is_wrong()
    {
        Definition.Judge(Multiple, [B, C, D]).ShouldBe(AnswerVerdict.Wrong);
        Definition.Judge(Single, [A, D]).ShouldBe(AnswerVerdict.Wrong);
    }

    [Fact]
    public void No_option_chosen_is_wrong()
    {
        Definition.Judge(Single, []).ShouldBe(AnswerVerdict.Wrong);
        Definition.Judge(Multiple, []).ShouldBe(AnswerVerdict.Wrong);
    }

    [Fact]
    public void A_question_belongs_to_its_chapter()
    {
        Definition.ChapterOf(Single).ShouldBe(Hygiene);
        Definition.ChapterOf(Multiple).ShouldBe(Gloves);
    }

    private static AnswerOptionId NewOption() => AnswerOptionId.From(Guid.CreateVersion7());
}
