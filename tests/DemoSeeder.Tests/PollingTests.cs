using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// The services learn of each other through events, so a call can be refused until a copy arrives: the seeder polls on the
/// codes that mean "not yet" (404 compliance.course-not-found, 403 learning.membership-missing), waiting on its
/// TimeProvider, and gives up after its limit naming the call and the code. Due dates are days in Germany.
/// </summary>
public class PollingTests
{
    [Fact]
    public async Task Eventual_answers_are_polled_until_they_settle()
    {
        var harness = new SeederHarness();
        harness.App.Api.CourseCopyMissing = 3;
        harness.App.Api.MembershipMissing = 2;
        var learner = Plans.Employees[3];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Started)));

        var assigns = harness.App.EdgeRequests.Where(request => request.Method == "POST" && request.Route == "/compliance/pharmacies/{id}/assignments").ToList();
        assigns.Select(request => request.Status).ShouldBe([404, 404, 404, 200]);
        AreApart(assigns).ShouldBeTrue("Each retry of the assignment waits on the TimeProvider: " + string.Join(", ", assigns.Select(request => request.At)));
        var starts = harness.App.EdgeRequests.Where(request => request.Method == "POST" && request.Route == "/learning/sessions").ToList();
        starts.Select(request => request.Status).ShouldBe([403, 403, 201]);
        AreApart(starts).ShouldBeTrue("Each retry of the start waits on the TimeProvider: " + string.Join(", ", starts.Select(request => request.At)));
        harness.App.Api.CompletedChapters(learner, Plans.Course).ShouldBe(["Kapitel 1"]);
    }

    [Fact]
    public async Task An_answer_that_never_settles_fails_the_run_after_the_limit_naming_the_call_and_the_code()
    {
        var harness = new SeederHarness();
        harness.App.Api.CourseCopyMissing = int.MaxValue;
        var limit = TimeSpan.FromMinutes(2);

        var exitCode = await harness.RunAsync(
            Plans.OnePharmacyOneCourse(Plans.Scenario(Plans.Employees[4], DemoOutcome.Open)),
            LocalSettings.Build(values => values["DemoSeeder:PollTimeout"] = limit.ToString()));

        exitCode.ShouldBe(1, $"Output:\n{harness.Output}");
        var assigns = harness.App.EdgeRequests.Where(request => request.Method == "POST" && request.Route == "/compliance/pharmacies/{id}/assignments").ToList();
        assigns.Count.ShouldBeGreaterThan(1, "The refusal was polled.");
        (assigns[^1].At - assigns[0].At).ShouldBeGreaterThanOrEqualTo(limit - TimeSpan.FromSeconds(30), "The seeder polls until its limit, by the TimeProvider's clock.");
        harness.Output.ToString().ShouldContain("compliance.course-not-found");
        harness.Output.ToString().ShouldContain("/compliance/pharmacies/");
    }

    [Fact]
    public async Task A_refusal_that_is_not_eventual_stops_the_run_at_once()
    {
        var harness = new SeederHarness();
        // The second approval answers 500: not a code the seeder waits on.
        harness.App.FailOnce(HttpMethod.Post, "/editorial/course-versions/{id}/questions/{questionId}/approve", after: 1);

        var exitCode = await harness.RunAsync(Plans.OnePharmacyOneCourse());

        exitCode.ShouldBe(1, $"Output:\n{harness.Output}");
        harness.App.EdgeRequests.Count(request => request.Route == "/editorial/course-versions/{id}/questions/{questionId}/approve").ShouldBe(2, "A POST is never retried: one attempt (docs/adr, the HttpClients' retry).");
        harness.App.EdgeRequests.ShouldNotContain(request => request.Route == "/editorial/course-versions/{id}/submit");
        harness.Output.ToString().ShouldContain("/approve");
    }

    [Fact]
    public async Task An_overdue_scenario_is_assigned_due_today_in_Germany()
    {
        var harness = new SeederHarness();
        var dueToday = Plans.Employees[4];
        var dueLater = Plans.Employees[0];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(
            Plans.Scenario(dueToday, DemoOutcome.Open, dueInDays: 0),
            Plans.Scenario(dueLater, DemoOutcome.Open, dueInDays: 30)));

        // 23:30 UTC on 10 March is 00:30 on 11 March in Germany: today there is the 11th.
        harness.App.Api.Assignments.Select(assignment => (assignment.User, assignment.Due)).ShouldBe(
            [(dueToday, new DateOnly(2026, 3, 11)), (dueLater, new DateOnly(2026, 4, 10))],
            ignoreOrder: true);
        harness.App.EdgeRequests.Where(request => request.Method == "POST" && request.Route == "/compliance/pharmacies/{id}/assignments")
            .ShouldAllBe(request => request.Status == 200, "No due date is refused as in the past.");
        harness.App.EdgeRequests.Where(request => request.Method == "POST" && request.Route == "/compliance/pharmacies/{id}/assignments")
            .Select(request => JsonNode.Parse(request.Body)?["dueDate"]?.GetValue<string>())
            .ShouldContain("2026-03-11");
    }

    private static bool AreApart(List<RecordedRequest> requests) =>
        requests.Zip(requests.Skip(1)).All(pair => pair.Second.At > pair.First.At);
}
