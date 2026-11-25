using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// A run looks every record up through the API before it writes: a seeded application gets only reads, and a run that
/// stopped half way is resumed from what the API shows, never started again.
/// </summary>
public class IdempotencyTests
{
    private static SeedPlan EveryOutcome() => Plans.OnePharmacyOneCourse(
        Plans.Scenario(Plans.Employees[0], DemoOutcome.Passed),
        Plans.Scenario(Plans.Employees[1], DemoOutcome.Failed),
        Plans.Scenario(Plans.Employees[2], DemoOutcome.Exhausted),
        Plans.Scenario(Plans.Employees[3], DemoOutcome.Started),
        Plans.Scenario(Plans.Employees[4], DemoOutcome.Open, dueInDays: 0));

    [Fact]
    public async Task A_seeded_application_gets_no_write()
    {
        var harness = new SeederHarness();
        await harness.SeedAsync(EveryOutcome());
        var changes = harness.App.Api.Changes;
        var mails = harness.App.Mail.Mails.Count;
        harness.App.Forget();
        harness.Output.GetStringBuilder().Clear();

        var exitCode = await harness.RunAsync(EveryOutcome());

        exitCode.ShouldBe(0, $"The second run failed. Output:\n{harness.Output}");
        harness.App.EdgeRequests.Where(request => request.IsWrite).Select(request => request.ToString())
            .ShouldBeEmpty("A second run issues only GETs to the edge.");
        harness.App.Api.Changes.ShouldBe(changes, "The edge stored nothing on the second run.");
        harness.App.Requests.ShouldNotContain(request => request.Origin == "mailpit", "Nothing to accept, no mail to read.");
        harness.App.Mail.Mails.Count.ShouldBe(mails, "No invitation was mailed again.");
        harness.Output.ToString().ShouldContain("Demo data: 0 created");
    }

    [Fact]
    public async Task An_open_invitation_is_accepted_from_the_newest_mail_without_inviting_again()
    {
        var harness = new SeederHarness();
        harness.App.FailOnce(HttpMethod.Post, "/organisation/invitations/accept");
        (await harness.RunAsync(Plans.OnePharmacyOneCourse())).ShouldNotBe(0, "The run should stop at the failed accept.");
        var open = harness.App.Api.OpenInvitations;
        open.ShouldNotBeEmpty("The failed accept leaves its invitation open.");
        // The manager renews them meanwhile: each address gets a second mail, and only its token is accepted.
        harness.App.Api.RenewOpenInvitations();
        harness.App.Forget();

        await harness.SeedAsync(Plans.OnePharmacyOneCourse());

        var invited = harness.App.EdgeRequests.Where(request => request.Method == "POST" && request.Route == "/organisation/pharmacies/{id}/invitations")
            .Select(request => JsonNode.Parse(request.Body)?["email"]?.GetValue<string>()).ToList();
        invited.Intersect(open).ShouldBeEmpty("An address with an open invitation is not invited again.");
        foreach (var address in open)
        {
            var newest = harness.App.Mail.Mails.Where(mail => mail.To == address).MaxBy(mail => mail.Created).ShouldNotBeNull();
            var token = newest.Text[(newest.Text.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..].Trim();
            var username = address[..address.IndexOf('@', StringComparison.Ordinal)];
            harness.App.EdgeRequests.Where(request => request.Route == "/organisation/invitations/accept" && request.User == username)
                .ShouldHaveSingleItem($"{username} accepts once.")
                .Body.ShouldContain(token, Case.Sensitive, $"{username} accepts with the token of the newest mail.");
        }

        harness.App.Api.PharmacyNames.ShouldBe([Plans.Pharmacy], "The pharmacy is found again, not registered twice.");
        harness.App.Api.MembersOf(Plans.Pharmacy).ShouldBe([Plans.Manager, .. Plans.Employees], ignoreOrder: true);
        harness.App.Api.OpenInvitations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_draft_left_half_way_gets_only_its_missing_chapters_and_questions()
    {
        var harness = new SeederHarness();
        harness.App.FailOnce(HttpMethod.Post, "/editorial/course-versions/{id}/questions", after: 1);
        (await harness.RunAsync(Plans.OnePharmacyOneCourse())).ShouldNotBe(0, "The run should stop at the second question.");
        var halfWay = harness.App.Api.ContentOf(Plans.Course, 1);
        halfWay.Chapters.ShouldBe(["Kapitel 1", "Kapitel 2"], "The first run leaves a draft with both chapters and one question.");
        halfWay.Questions.ShouldBe(["Frage zu Kapitel 1?"], "The first run leaves a draft with both chapters and one question.");
        harness.App.Forget();

        await harness.SeedAsync(Plans.OnePharmacyOneCourse());

        var editorialWrites = harness.App.EdgeRequests.Where(request => request.IsWrite && request.Route.StartsWith("/editorial/", StringComparison.Ordinal)).Select(request => (request.Method, request.Route)).ToList();
        editorialWrites.ShouldNotContain(("POST", "/editorial/courses"), "The draft is found by its title, not drafted again.");
        editorialWrites.ShouldNotContain(("POST", "/editorial/course-versions/{id}/chapters"), "Both chapters are there.");
        editorialWrites.ShouldNotContain(("PUT", "/editorial/course-versions/{id}/blueprint"), "The blueprint is set as planned.");
        editorialWrites.Count(write => write == ("POST", "/editorial/course-versions/{id}/questions")).ShouldBe(1, "Only the missing question is added.");
        harness.App.Api.Versions.ShouldBe([(Plans.Course, 1, "Published")]);
        var resumed = harness.App.Api.ContentOf(Plans.Course, 1);
        resumed.Chapters.ShouldBe(["Kapitel 1", "Kapitel 2"]);
        resumed.Questions.ShouldBe(["Frage zu Kapitel 1?", "Frage zu Kapitel 2?"]);
    }

    [Fact]
    public async Task An_examination_left_half_way_is_continued_to_its_outcome()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[2];
        var plan = Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Exhausted));
        harness.App.FailOnce(HttpMethod.Post, "/assessment/examinations/{id}/attempts/current/submit", after: 1);
        (await harness.RunAsync(plan)).ShouldNotBe(0, "The run should stop at the second attempt's submit.");
        harness.App.Forget();

        await harness.SeedAsync(plan);

        harness.App.Api.Examinations.ShouldBe([(learner, Plans.Course, 1, "Exhausted", FakeEdge.MaxAttempts)], "The examination is resumed, not started again.");
        harness.App.EdgeRequests.ShouldNotContain(request => request.Route == "/learning/sessions/{id}/progress", "The content was completed already.");
        harness.App.Api.Assignments.Count.ShouldBe(1, "The course is not assigned again.");
    }

    [Fact]
    public async Task An_exhausted_examination_gets_no_progress_report()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[2];
        var plan = Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Exhausted));
        await harness.SeedAsync(plan);
        harness.App.Api.Examinations.ShouldBe([(learner, Plans.Course, 1, "Exhausted", FakeEdge.MaxAttempts)]);
        harness.App.Forget();

        await harness.SeedAsync(plan);

        harness.App.EdgeRequests.ShouldNotContain(request => request.Route == "/learning/sessions/{id}/progress", "Progress on an exhausted examination's session would open a new round.");
        harness.App.Api.ProgressAfterExhaustion.ShouldBe(0);
    }
}
