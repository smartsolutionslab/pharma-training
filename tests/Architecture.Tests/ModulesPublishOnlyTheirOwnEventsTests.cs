using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class ModulesPublishOnlyTheirOwnEventsTests
{
    [Fact]
    public void A_module_that_creates_another_modules_event_fails_the_rule()
    {
        var violations = ModulesPublishOnlyTheirOwnEvents.Violations("Catalog", [typeof(ProbeTestDefinitionPublisher)]).ToList();

        var violation = violations.ShouldHaveSingleItem();
        violation.ShouldContain(nameof(ProbeTestDefinitionPublisher));
        violation.ShouldContain(typeof(TestDefinitionPublishedV1).FullName!);
    }

    [Fact]
    public void A_module_that_creates_its_own_event_passes_the_rule()
    {
        ModulesPublishOnlyTheirOwnEvents.Violations("Editorial", [typeof(ProbeTestDefinitionPublisher)]).ShouldBeEmpty();
    }

    [Fact]
    public void The_module_is_matched_ignoring_case()
    {
        ModulesPublishOnlyTheirOwnEvents.Violations("editorial", [typeof(ProbeTestDefinitionPublisher)]).ShouldBeEmpty();
    }

    [Fact]
    public void An_event_created_in_a_lambda_is_seen()
    {
        var closures = typeof(ProbeTestDefinitionPublisher).GetNestedTypes(System.Reflection.BindingFlags.NonPublic);

        closures.ShouldNotBeEmpty("the probe's lambda compiled into no nested type");
        ModulesPublishOnlyTheirOwnEvents.Violations("Catalog", closures).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_module_whose_namespace_only_begins_with_the_events_fails_the_rule()
    {
        ModulesPublishOnlyTheirOwnEvents.Violations("Edit", [typeof(ProbeTestDefinitionPublisher)]).ShouldHaveSingleItem();
    }
}
