using System.Runtime.CompilerServices;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
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
    public void A_member_naming_a_solution_is_named()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeSolutionResponse)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain($"{nameof(ProbeSolutionResponse)}.{nameof(ProbeSolutionResponse.Solution)}");
    }

    [Fact]
    public void A_member_naming_an_answer_key_is_named()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeLeakyQuestion)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain($"{nameof(ProbeLeakyQuestion)}.{nameof(ProbeLeakyQuestion.AnswerKey)}");
    }

    [Fact]
    public void A_query_result_reaching_an_answer_key_is_named_with_its_path()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeGetLeakyPaper)]).ToList();

        violations.ShouldContain(violation =>
            violation.Contains($"{nameof(ProbeGetLeakyPaper)}.Result", StringComparison.Ordinal)
            && violation.Contains("Questions[].AnswerKey", StringComparison.Ordinal));
    }

    [Fact]
    public void A_command_result_reaching_a_solution_is_named()
    {
        var violations = AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeSubmitRevealingSolution)]).ToList();

        violations.ShouldContain(violation =>
            violation.Contains($"{nameof(ProbeSubmitRevealingSolution)}.Result", StringComparison.Ordinal)
            && violation.Contains($".{nameof(ProbeSolutionResponse.Solution)}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_response_type_without_solutions_passes() =>
        AssessmentResponsesCarryNoSolutions.Violations([typeof(ProbeAttemptResultResponse)]).ShouldBeEmpty();

    [Fact]
    public void The_rule_roots_at_every_request_and_dto_of_the_application_every_api_type_and_every_assessment_event()
    {
        var model = ArchitectureModel.Load();

        var roots = AssessmentResponsesCarryNoSolutions.Roots(model);

        var application = TypesOf(model, "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application");
        var requests = application.Where(type => !type.IsAbstract && type.GetInterfaces().Any(IsRequest)).ToList();
        var dtos = application.Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal)).ToList();
        var api = TypesOf(model, "SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api");
        var events = typeof(TestPassedV1).Assembly.GetTypes()
            .Where(type => type.Namespace == "SmartSolutionsLab.PharmaTraining.Contracts.Assessment" && !IsCompilerGenerated(type))
            .ToList();
        requests.ShouldNotBeEmpty("the Application has requests, so the assertion means something");
        foreach (var type in requests.Concat(dtos).Concat(api).Concat(events))
        {
            roots.ShouldContain(type);
        }
    }

    [Fact]
    public void The_rule_passes_on_the_assessment_module() =>
        new AssessmentResponsesCarryNoSolutions().Violations(ArchitectureModel.Load()).ShouldBeEmpty();

    private static List<Type> TypesOf(ArchitectureModel model, string assembly) =>
        model.ModuleAssemblies
            .Where(candidate => candidate.GetName().Name == assembly)
            .SelectMany(candidate => candidate.GetTypes())
            .Where(type => !IsCompilerGenerated(type))
            .ToList();

    private static bool IsRequest(Type contract) =>
        contract.IsGenericType
        && !contract.ContainsGenericParameters
        && (contract.GetGenericTypeDefinition() == typeof(IQuery<>) || contract.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static bool IsCompilerGenerated(Type type) => type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
}
