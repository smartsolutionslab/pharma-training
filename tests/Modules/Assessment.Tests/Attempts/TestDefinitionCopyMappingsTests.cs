using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using static SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts.AttemptFixture;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>A version's copy becomes the definition the aggregate scores an attempt against: in position order, with exactly its correct options.</summary>
public class TestDefinitionCopyMappingsTests
{
    private readonly AttemptFixture fixture = new();

    [Fact]
    public void A_copy_becomes_the_definition_it_scores_against()
    {
        var definition = fixture.Copy().ToDefinition();

        definition.CourseVersion.ShouldBe(fixture.Version);
        definition.PassingScore.ShouldBe(ScorePercent.From(80));
        definition.MaxAttempts.ShouldBe(AttemptNumber.From(3));
        definition.Questions.Select(question => question.Question).ShouldBe([Q1, Q2]);
        definition.Questions[0].Chapter.ShouldBe(Hygiene);
        definition.Questions[0].Options.ShouldBe([A, D]);
        definition.Questions[0].CorrectOptions.ShouldBe([A]);
        definition.Questions[1].Chapter.ShouldBe(Gloves);
        definition.Questions[1].Options.ShouldBe([B, C, E]);
        definition.Questions[1].CorrectOptions.ShouldBe([B, C]);
    }

    [Fact]
    public void A_single_choice_copy_with_its_one_right_option_judges_exactly_it_right()
    {
        var definition = fixture.Copy().ToDefinition();

        definition.Judge(Q1, [A]).ShouldBe(AnswerVerdict.Right);
        definition.Judge(Q1, [D]).ShouldBe(AnswerVerdict.Wrong);
        definition.Judge(Q1, [A, D]).ShouldBe(AnswerVerdict.Wrong);
        definition.Judge(Q2, [B, C]).ShouldBe(AnswerVerdict.Right);
        definition.Judge(Q2, [B]).ShouldBe(AnswerVerdict.Wrong);
    }
}
