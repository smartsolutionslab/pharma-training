using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// An attempt shows the options shuffled, under ids of its own; the seeder finds the option by the question's and the
/// option's text, and answers the one the plan marks correct to pass, a wrong one to fail.
/// </summary>
public class ExaminationTests
{
    [Fact]
    public async Task A_passing_attempt_answers_the_options_the_plan_marks_correct_whatever_their_order()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[0];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Passed)));

        var chosen = Answers(harness, learner);
        chosen.Count.ShouldBe(2, "One answer per question of the blueprint.");
        chosen.ShouldAllBe(option => harness.App.Api.IsCorrect(option), "Every answer is the option the plan marks correct, wherever the attempt shows it.");
        harness.App.Api.Examinations.ShouldBe([(learner, Plans.Course, 1, "Passed", 1)]);
        harness.App.Api.Certificates.ShouldBe([(learner, Plans.Course)]);
    }

    [Fact]
    public async Task A_failing_attempt_answers_options_the_plan_marks_wrong()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[1];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Failed)));

        var chosen = Answers(harness, learner);
        chosen.Count.ShouldBe(2);
        chosen.ShouldAllBe(option => !harness.App.Api.IsCorrect(option), "A failed attempt answers wrong options.");
        harness.App.Api.Examinations.ShouldBe([(learner, Plans.Course, 1, "Unlocked", 1)], "One failed attempt, two left.");
        harness.App.Api.Certificates.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_exhausting_learner_fails_every_attempt()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[2];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Exhausted)));

        harness.App.EdgeRequests.Count(request => request.Route == "/assessment/examinations/{id}/attempts/current/submit" && request.User == learner).ShouldBe(FakeEdge.MaxAttempts);
        Answers(harness, learner).ShouldAllBe(option => !harness.App.Api.IsCorrect(option));
        harness.App.Api.Examinations.ShouldBe([(learner, Plans.Course, 1, "Exhausted", FakeEdge.MaxAttempts)]);
        harness.App.Api.ProgressAfterExhaustion.ShouldBe(0);
    }

    [Fact]
    public async Task A_learner_who_starts_completes_the_first_chapter_alone_and_takes_no_examination()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[3];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Started)));

        harness.App.Api.CompletedChapters(learner, Plans.Course).ShouldBe(["Kapitel 1"]);
        harness.App.EdgeRequests.ShouldNotContain(request => request.User == learner && request.Route.StartsWith("/assessment/", StringComparison.Ordinal) && request.IsWrite);
    }

    [Fact]
    public async Task An_open_assignment_is_never_started()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[4];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Open)));

        harness.App.Api.Assignments.ShouldHaveSingleItem().User.ShouldBe(learner);
        harness.App.EdgeRequests.ShouldNotContain(request => request.User == learner && request.Route.StartsWith("/learning/", StringComparison.Ordinal), "Open scenarios never call POST /learning/sessions.");
    }

    /// <summary>The option ids the learner answered, in order.</summary>
    private static List<string> Answers(SeederHarness harness, string learner) =>
        [.. harness.App.EdgeRequests
            .Where(request => request.Method == "PUT" && request.Route == "/assessment/examinations/{id}/attempts/current/answers/{questionId}" && request.User == learner)
            .SelectMany(request => JsonNode.Parse(request.Body)?["optionIds"]?.AsArray() ?? [])
            .Select(option => option?.GetValue<string>() ?? string.Empty)];
}
