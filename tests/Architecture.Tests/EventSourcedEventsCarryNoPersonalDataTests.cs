using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes.Nested;
using SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests;

public class EventSourcedEventsCarryNoPersonalDataTests
{
    [Fact]
    public void A_learning_or_assessment_event_with_a_name_fails_the_rule()
    {
        var violations = EventSourcedEventsCarryNoPersonalData.Violations([typeof(LearningProbeWithDisplayNameV1)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain("DisplayName");
    }

    [Fact]
    public void An_event_with_an_email_fails_the_rule()
    {
        var violations = EventSourcedEventsCarryNoPersonalData.Violations([typeof(AssessmentProbeWithEmailV1)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain("Email");
    }

    [Fact]
    public void A_name_inside_a_listed_record_fails_the_rule()
    {
        var violations = EventSourcedEventsCarryNoPersonalData.Violations([typeof(ProbeWithNestedNameV1)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain("HolderName");
    }

    [Fact]
    public void A_name_inside_a_listed_struct_fails_the_rule()
    {
        var violations = EventSourcedEventsCarryNoPersonalData.Violations([typeof(ProbeWithBadgesV1)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain("HolderName");
    }

    [Fact]
    public void A_name_inside_an_optional_struct_fails_the_rule()
    {
        var violations = EventSourcedEventsCarryNoPersonalData.Violations([typeof(ProbeWithOptionalBadgeV1)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain("HolderName");
    }

    [Fact]
    public void A_name_inside_a_dictionary_fails_the_rule()
    {
        var violations = EventSourcedEventsCarryNoPersonalData.Violations([typeof(ProbeWithMapV1)]).ToList();

        violations.ShouldHaveSingleItem().ShouldContain("HolderName");
    }

    [Fact]
    public void The_rule_also_checks_events_in_a_namespace_below_a_module()
    {
        var events = EventSourcedEventsCarryNoPersonalData.EventsIn(
            [typeof(ProbeWithIdsOnlyV1), typeof(NestedProbeWithIdsV1), typeof(ProbeAttendeeV1)],
            ["SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes"]);

        events.ShouldBe([typeof(ProbeWithIdsOnlyV1), typeof(NestedProbeWithIdsV1)], ignoreOrder: true);
    }

    [Fact]
    public void A_namespace_that_only_starts_with_the_same_letters_is_not_a_module()
    {
        var events = EventSourcedEventsCarryNoPersonalData.EventsIn([typeof(ProbeWithIdsOnlyV1)], ["SmartSolutionsLab.PharmaTraining.Architecture.Tests.Prob"]);

        events.ShouldBeEmpty();
    }

    [Fact]
    public void An_event_with_only_ids_passes_the_rule()
    {
        EventSourcedEventsCarryNoPersonalData.Violations([typeof(ProbeWithIdsOnlyV1)]).ShouldBeEmpty();
    }

    [Fact]
    public void The_rule_checks_every_event_of_learning_and_assessment()
    {
        var names = EventSourcedEventsCarryNoPersonalData.EventsOfEventSourcedModules().Select(type => type.Name).ToList();

        names.ShouldBe(["LearningSessionStartedV1", "ContentCompletedV1", "TestPassedV1", "TestFailedV1", "AttemptsExhaustedV1"], ignoreOrder: true);
    }
}
