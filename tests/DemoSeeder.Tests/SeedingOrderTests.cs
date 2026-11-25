using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// An empty application seeded step by step, through the edge as the people the plan names: users first through the admin
/// API, then each pharmacy (register, invite, read the mail, accept), each course (draft, chapters, blueprint, questions,
/// approve, submit, publish: three people, so four eyes holds) and each scenario (assign, learn, examine).
/// </summary>
public class SeedingOrderTests
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task An_empty_application_is_seeded_in_order()
    {
        var harness = new SeederHarness();

        await harness.SeedAsync(Plans.OnePharmacyOneCourse());

        var requests = harness.App.Requests;
        foreach (var user in Plans.OnePharmacyOneCourse().Users)
        {
            var realmUser = harness.App.Identity.Users.SingleOrDefault(each => each.Username == user.Username).ShouldNotBeNull($"{user.Username} was not created.");
            realmUser.ShouldSatisfyAllConditions(
                each => each.Email.ShouldBe(user.Email),
                each => each.FirstName.ShouldBe(user.FirstName),
                each => each.LastName.ShouldBe(user.LastName),
                each => each.Enabled.ShouldBeTrue(),
                each => each.EmailVerified.ShouldBeTrue(),
                each => each.Password.ShouldBe(user.Username, "Locally a demo user's password is their username."));
            if (user.RealmRole is not null)
            {
                realmUser.Roles.ShouldContain(user.RealmRole);
            }

            var created = Index(requests, request => request.Origin == "keycloak" && request.Method == "POST" && request.Route == "/users" && request.Body.Contains($"\"{user.Username}\"", StringComparison.Ordinal));
            var signedIn = Index(requests, request => request.Route == "login" && request.User == user.Username);
            created.ShouldBeGreaterThanOrEqualTo(0);
            signedIn.ShouldBeGreaterThan(created, $"{user.Username} signed in before the admin API created them.");
        }

        Writes(harness, "/organisation/").First().ShouldBe(("POST", "/organisation/pharmacies", Plans.Manager));
        foreach (var employee in Plans.Employees)
        {
            var invited = Index(requests, request => request.Route == "/organisation/pharmacies/{id}/invitations" && request.Method == "POST" && request.Body.Contains($"{employee}@example.com", StringComparison.Ordinal));
            var read = Index(requests, request => request.Origin == "mailpit" && Uri.UnescapeDataString(request.Query).Contains($"to:{employee}@example.com", StringComparison.Ordinal));
            var accepted = Index(requests, request => request.Route == "/organisation/invitations/accept" && request.User == employee);
            invited.ShouldBeGreaterThanOrEqualTo(0, $"{employee} was not invited.");
            read.ShouldBeGreaterThan(invited, $"{employee}'s mail was read before the invitation.");
            accepted.ShouldBeGreaterThan(read, $"{employee} accepted before the mail was read.");
            requests[invited].User.ShouldBe(Plans.Manager);
        }

        harness.App.Api.MembersOf(Plans.Pharmacy).ShouldBe([Plans.Manager, .. Plans.Employees], ignoreOrder: true);

        Writes(harness, "/editorial/").ShouldBe(
        [
            ("POST", "/editorial/courses", Plans.Author),
            ("POST", "/editorial/course-versions/{id}/chapters", Plans.Author),
            ("POST", "/editorial/course-versions/{id}/chapters", Plans.Author),
            ("PUT", "/editorial/course-versions/{id}/blueprint", Plans.Author),
            ("POST", "/editorial/course-versions/{id}/questions", Plans.Author),
            ("POST", "/editorial/course-versions/{id}/questions", Plans.Author),
            ("POST", "/editorial/course-versions/{id}/questions/{questionId}/approve", Plans.Approver),
            ("POST", "/editorial/course-versions/{id}/questions/{questionId}/approve", Plans.Approver),
            ("POST", "/editorial/course-versions/{id}/submit", Plans.Author),
            ("POST", "/editorial/course-versions/{id}/publish", Plans.Publisher),
        ]);
        harness.App.Api.Versions.ShouldBe([(Plans.Course, 1, "Published")]);
        harness.App.Api.ContentOf(Plans.Course, 1).ShouldBe((["Kapitel 1", "Kapitel 2"], ["Frage zu Kapitel 1?", "Frage zu Kapitel 2?"]));
        harness.App.Api.BlueprintChangedAfterApproval.ShouldBeFalse("The blueprint is set before any approval (docs/adr/0007).");
        harness.App.Requests.Where(request => request.Origin == "edge").ShouldAllBe(request => request.Status < 400, "No call of an empty seed is refused.");
        Regex.IsMatch(harness.Output.ToString(), @"Demo data: [1-9]\d* created", RegexOptions.None, MatchTimeout).ShouldBeTrue($"The summary line reports what was created. Output:\n{harness.Output}");
    }

    [Fact]
    public async Task A_passing_learner_learns_then_takes_the_examination_then_has_the_certificate()
    {
        var harness = new SeederHarness();
        var learner = Plans.Employees[0];

        await harness.SeedAsync(Plans.OnePharmacyOneCourse(Plans.Scenario(learner, DemoOutcome.Passed)));

        var assigned = Index(harness.App.Requests, request => request.Route == "/compliance/pharmacies/{id}/assignments" && request.Method == "POST" && request.Status == 200);
        assigned.ShouldBeGreaterThanOrEqualTo(0, "The course was never assigned.");
        harness.App.Requests[assigned].User.ShouldBe(Plans.Manager);
        var learnerWrites = harness.App.Requests.Select((request, index) => (request, index)).Where(each => each.request.Origin == "edge" && each.request.IsWrite && each.request.User == learner).ToList();
        learnerWrites.Select(each => (each.request.Method, each.request.Route)).Where(each => each.Route != "/organisation/invitations/accept").ShouldBe(
        [
            ("POST", "/learning/sessions"),
            ("POST", "/learning/sessions/{id}/progress"),
            ("POST", "/learning/sessions/{id}/progress"),
            ("POST", "/assessment/examinations/{id}/attempts"),
            ("PUT", "/assessment/examinations/{id}/attempts/current/answers/{questionId}"),
            ("PUT", "/assessment/examinations/{id}/attempts/current/answers/{questionId}"),
            ("POST", "/assessment/examinations/{id}/attempts/current/submit"),
        ]);
        learnerWrites.First(each => each.request.Route == "/learning/sessions").index.ShouldBeGreaterThan(assigned, "The learner starts only once the course is assigned.");
        harness.App.Api.Assignments.ShouldBe([(learner, Plans.Course, new DateOnly(2026, 4, 10), (int?)null)]);
        harness.App.Api.Certificates.ShouldBe([(learner, Plans.Course)]);
    }

    [Fact]
    public async Task The_first_version_is_passed_before_the_second_is_published()
    {
        var harness = new SeederHarness();
        var first = Plans.Employees[0];
        var second = Plans.Employees[1];

        await harness.SeedAsync(Plans.WithSecondVersion(
            Plans.Scenario(first, DemoOutcome.Passed, version: 1),
            Plans.Scenario(second, DemoOutcome.Passed, version: 2)));

        harness.App.Api.Versions.ShouldBe([(Plans.Course, 1, "Superseded"), (Plans.Course, 2, "Published")], ignoreOrder: true);
        harness.App.Api.ContentOf(Plans.Course, 2).ShouldBe((["Kapitel 1", "Kapitel 2", "Kapitel 3"], ["Frage zu Kapitel 1?", "Frage zu Kapitel 2?", "Frage zu Kapitel 3?"]));
        harness.App.Api.Examinations.Select(exam => (exam.User, exam.Version, exam.Status)).ShouldBe([(first, 1, "Passed"), (second, 2, "Passed")], ignoreOrder: true);
        harness.App.Api.Certificates.ShouldBe([(first, Plans.Course), (second, Plans.Course)], ignoreOrder: true);

        var requests = harness.App.Requests;
        var passedFirst = Index(requests, request => request.Route == "/assessment/examinations/{id}/attempts/current/submit" && request.User == first);
        var publishes = requests.Select((request, index) => (request, index)).Where(each => each.request.Route == "/editorial/course-versions/{id}/publish" && each.request.Status == 200).Select(each => each.index).ToList();
        publishes.Count.ShouldBe(2);
        passedFirst.ShouldBeInRange(publishes[0], publishes[1], "The first version's learner passes after it is published and before the second version is.");
        Writes(harness, "/editorial/").Where(write => write.Route.EndsWith("/next", StringComparison.Ordinal)).ShouldBe([("POST", "/editorial/course-versions/{id}/next", Plans.Author)]);
        Writes(harness, "/editorial/").Where(write => write.Route.EndsWith("/approve", StringComparison.Ordinal)).ShouldAllBe(write => write.User == Plans.Approver);
        Writes(harness, "/editorial/").Where(write => write.Route.EndsWith("/publish", StringComparison.Ordinal)).ShouldAllBe(write => write.User == Plans.Publisher);
    }

    [Fact]
    public async Task Every_route_the_seeder_calls_is_in_the_openapi_document()
    {
        var harness = new SeederHarness();

        await harness.SeedAsync(Plans.WithSecondVersion(
            Plans.Scenario(Plans.Employees[0], DemoOutcome.Passed),
            Plans.Scenario(Plans.Employees[1], DemoOutcome.Failed),
            Plans.Scenario(Plans.Employees[2], DemoOutcome.Exhausted),
            Plans.Scenario(Plans.Employees[3], DemoOutcome.Started, version: 2),
            Plans.Scenario(Plans.Employees[4], DemoOutcome.Open, dueInDays: 0, recurrenceMonths: 12)));

        var documented = OpenApiRoutes();
        var called = harness.App.EdgeRequests.Select(request => (request.Method, request.Path)).Distinct().ToList();
        called.ShouldNotBeEmpty();
        called.Where(call => !documented.Any(route => route.Method == call.Method && Regex.IsMatch(call.Path, route.Pattern, RegexOptions.None, MatchTimeout)))
            .ShouldBeEmpty("The seeder calls a route web/app/openapi.json does not describe; it uses the edge's public API alone.");
        harness.App.Requests.ShouldNotContain(request => request.Status == 501, "Every call reaches a route of the fake: the edge's under /api.");
    }

    /// <summary>The edge's writes whose route starts with the prefix, as method, route template and user, in order.</summary>
    private static List<(string Method, string Route, string? User)> Writes(SeederHarness harness, string prefix) =>
        [.. harness.App.EdgeRequests.Where(request => request.IsWrite && request.Route.StartsWith(prefix, StringComparison.Ordinal)).Select(request => (request.Method, request.Route, request.User))];

    private static int Index(IReadOnlyList<RecordedRequest> requests, Func<RecordedRequest, bool> which) =>
        requests.Select((request, index) => (request, index)).Where(each => which(each.request)).Select(each => each.index).DefaultIfEmpty(-1).First();

    /// <summary>Every route of web/app/openapi.json as its method and a pattern of its path.</summary>
    private static List<(string Method, string Pattern)> OpenApiRoutes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.proj")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(root?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output."), "web", "app", "openapi.json");
        var document = JsonNode.Parse(File.ReadAllText(path)) ?? throw new JsonException($"{path} is empty.");
        List<(string Method, string Pattern)> routes = [];
        foreach (var (template, operations) in document["paths"]?.AsObject() ?? [])
        {
            var pattern = "^" + Regex.Replace(Regex.Escape(template), @"\\\{\w+}", "[^/]+", RegexOptions.None, MatchTimeout) + "$";
            foreach (var (method, _) in operations?.AsObject() ?? [])
            {
                routes.Add((method.ToUpperInvariant(), pattern));
            }
        }

        return routes;
    }
}
