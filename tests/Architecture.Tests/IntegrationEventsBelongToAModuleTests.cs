using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class IntegrationEventsBelongToAModuleTests
{
    [Fact]
    public void An_event_outside_the_contract_namespaces_fails_the_rule()
    {
        var violations = IntegrationEventsBelongToAModule.Violations([typeof(ProbeStrayEventV1)], ["Editorial"]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain(nameof(ProbeStrayEventV1));
    }

    [Fact]
    public void An_event_in_the_namespace_of_a_module_that_does_not_exist_fails_the_rule()
    {
        var violations = IntegrationEventsBelongToAModule.Violations([typeof(TestDefinitionPublishedV1)], ["Catalog"]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain(nameof(TestDefinitionPublishedV1));
    }

    [Fact]
    public void An_event_in_its_modules_namespace_passes_the_rule()
    {
        IntegrationEventsBelongToAModule.Violations([typeof(TestDefinitionPublishedV1)], ["editorial"]).ShouldBeEmpty();
    }

    [Fact]
    public void A_payload_type_that_is_no_event_is_not_checked()
    {
        IntegrationEventsBelongToAModule.Violations([typeof(AnswerOptionV1), typeof(ProbeTestDefinitionPublisher)], []).ShouldBeEmpty();
    }
}
