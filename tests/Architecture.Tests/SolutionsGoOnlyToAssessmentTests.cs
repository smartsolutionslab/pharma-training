using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class SolutionsGoOnlyToAssessmentTests
{
    [Fact]
    public void A_consumer_of_the_test_definition_outside_assessment_fails_the_rule()
    {
        var violations = SolutionsGoOnlyToAssessment.Violations([typeof(ProbeTestDefinitionConsumer)], "Assessment.").ToList();

        violations.ShouldHaveSingleItem().ShouldContain(nameof(ProbeTestDefinitionConsumer));
    }

    [Fact]
    public void A_consumer_of_the_test_definition_inside_the_allowed_module_passes_the_rule()
    {
        SolutionsGoOnlyToAssessment.Violations([typeof(ProbeTestDefinitionConsumer)], "Architecture.").ShouldBeEmpty();
    }

    [Fact]
    public void A_consumer_that_inherits_its_handler_fails_the_rule()
    {
        var violations = SolutionsGoOnlyToAssessment.Violations([typeof(ProbeInheritedTestDefinitionConsumer)], "Assessment.").ToList();

        violations.ShouldHaveSingleItem().ShouldContain(nameof(ProbeInheritedTestDefinitionConsumer));
    }

    [Fact]
    public void The_allowed_module_is_the_real_assessment_application_and_no_other()
    {
        var allowed = ArchitectureModel.Load().ModuleAssemblies
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name?.StartsWith(SolutionsGoOnlyToAssessment.AllowedAssemblies, StringComparison.Ordinal) == true)
            .ToList();

        allowed.ShouldBe(["SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application"]);
    }

    [Fact]
    public void A_consumer_of_the_course_content_anywhere_passes_the_rule()
    {
        SolutionsGoOnlyToAssessment.Violations([typeof(ProbeCourseVersionConsumer)], "Assessment.").ShouldBeEmpty();
    }
}
