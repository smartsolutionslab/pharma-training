using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class AssessmentResponsesCarryNoSolutionsTests
{
    [Fact]
    public void A_response_type_with_a_correctness_member_is_named()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeAnswerVerdictResponse)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain($"{nameof(ProbeAnswerVerdictResponse)}.{nameof(ProbeAnswerVerdictResponse.IsCorrect)}");
    }

    [Fact]
    public void A_correctness_member_nested_in_a_list_is_named_with_its_path()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeQuestionPaperResponse)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain($"{nameof(ProbeQuestionPaperResponse)}.Questions[].Options[].IsCorrect");
    }

    [Fact]
    public void A_response_type_carrying_the_test_definition_is_named()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeResponseWithDefinition)]).ToList();

        violations.ShouldContain(violation =>
            violation.Contains($"{nameof(ProbeResponseWithDefinition)}.{nameof(ProbeResponseWithDefinition.Definition)}", StringComparison.Ordinal)
            && violation.Contains(nameof(TestDefinition), StringComparison.Ordinal));
    }

    [Fact]
    public void A_response_type_without_solutions_passes() =>
        AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeAttemptResultResponse)]).ShouldBeEmpty();

    [Fact]
    public void The_rule_passes_on_the_assessment_module() =>
        new AssessmentResponsesCarryNoSolutions().Violations(ArchitectureModel.Load()).ShouldBeEmpty();
}
