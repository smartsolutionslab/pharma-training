using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Drives a scenario: the manager assigns the course to the learners who hold neither a certificate of it nor an
/// assignment, due a number of days after today in Germany; then each learner does what the scenario says. What the API
/// shows decides what is left: a certificate is done; an assignment InProgress is done for a learner who starts or fails
/// once, Locked for one who exhausts the examination. A learner who acted is waited for until Compliance shows the outcome.
/// </summary>
internal sealed class ScenarioSeeding(
    SeedPlan plan,
    EdgeClient edge,
    Poller poller,
    PharmacySeeding pharmacies,
    CourseSeeding courses,
    TimeProvider time,
    SeedCount count,
    TextWriter output)
{
    private static readonly TimeZoneInfo Germany = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly Dictionary<string, DemoUser> users = plan.Users.ToDictionary(user => user.Username, StringComparer.Ordinal);

    public async Task DriveAsync(DemoScenario scenario, CancellationToken cancellationToken)
    {
        var pharmacy = plan.Pharmacies.Single(each => each.Name == scenario.Pharmacy);
        var pharmacyId = pharmacies.IdOf(pharmacy.Name);
        var course = plan.Courses.Single(each => each.Title == scenario.Course);
        var version = courses.Version(course.Title, scenario.CourseVersion);

        Dictionary<string, string> states = [with(StringComparer.Ordinal)];
        List<string> unassigned = [];
        foreach (var learner in scenario.Learners)
        {
            if (await HasCertificateAsync(learner, course.Title, requireDocument: false, cancellationToken))
            {
                continue;
            }

            var assignment = await AssignmentAsync(learner, pharmacyId, version.CourseId, cancellationToken);
            if (assignment is null)
            {
                unassigned.Add(learner);
                states[learner] = "Open";
            }
            else
            {
                states[learner] = Json.Text(assignment, "state") ?? "Open";
            }
        }

        if (unassigned.Count > 0)
        {
            await AssignAsync(scenario, pharmacy, pharmacyId, version, unassigned, cancellationToken);
        }

        foreach (var (learner, state) in states)
        {
            await LearnAsync(learner, scenario.Outcome, state, pharmacyId, version, course, cancellationToken);
        }
    }

    private async Task AssignAsync(DemoScenario scenario, DemoPharmacy pharmacy, string pharmacyId, SeededVersion version, List<string> learners, CancellationToken cancellationToken)
    {
        var persons = await PersonsAsync(pharmacy, pharmacyId, learners, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Germany).DateTime);
        var body = new JsonObject
        {
            ["courseId"] = version.CourseId,
            ["personIds"] = new JsonArray([.. learners.Select(learner => (JsonNode)persons[learner])]),
            ["dueDate"] = today.AddDays(scenario.DueInDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["recurrenceMonths"] = scenario.RecurrenceMonths,
        };
        var answer = await edge.CallAsync(pharmacy.Manager, HttpMethod.Post, $"/compliance/pharmacies/{pharmacyId}/assignments", body, cancellationToken, "compliance.course-not-found");
        var refused = Json.Items(answer.Body, "refused").Where(each => Json.Text(each, "code") != "compliance.already-assigned").ToList();
        if (refused.Count > 0)
        {
            throw new SeedingException($"Assigning {scenario.Course} in {pharmacy.Name} was refused: {string.Join(", ", refused.Select(each => Json.Text(each, "code")))}.");
        }

        count.Add(Json.Items(answer.Body, "assigned").Count);
        output.WriteLine($"Demo data: assigned {scenario.Course} in {pharmacy.Name} to {learners.Count}.");
    }

    /// <summary>The learners' person ids, by name, once Compliance lists every one of them as a member.</summary>
    private Task<Dictionary<string, string>> PersonsAsync(DemoPharmacy pharmacy, string pharmacyId, List<string> learners, CancellationToken cancellationToken) =>
        poller.UntilAsync($"The members of {pharmacy.Name} in Compliance", async token =>
        {
            var members = await edge.AllAsync(pharmacy.Manager, $"/compliance/pharmacies/{pharmacyId}/members", token);
            var byName = members.GroupBy(member => Json.Text(member, "name") ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => Json.Text(group.First(), "personId"), StringComparer.Ordinal);
            Dictionary<string, string> persons = [with(StringComparer.Ordinal)];
            foreach (var learner in learners)
            {
                if (byName.GetValueOrDefault(NameOf(learner)) is not { } person)
                {
                    return Polled<Dictionary<string, string>>.Wait($"without {learner}");
                }

                persons[learner] = person;
            }

            return Polled<Dictionary<string, string>>.Done(persons);
        }, cancellationToken);

    private async Task LearnAsync(string learner, DemoOutcome outcome, string state, string pharmacyId, SeededVersion version, DemoCourse course, CancellationToken cancellationToken)
    {
        var done = outcome switch
        {
            DemoOutcome.Open => true,
            DemoOutcome.Started or DemoOutcome.Failed => state == "InProgress",
            DemoOutcome.Exhausted => state == "Locked",
            DemoOutcome.Passed => false,
            _ => throw new SeedingException($"{outcome} is no outcome the seeder drives."),
        };
        if (done)
        {
            return;
        }

        var start = await edge.CallAsync(learner, HttpMethod.Post, "/learning/sessions", new JsonObject { ["courseVersionId"] = version.Id }, cancellationToken, "learning.membership-missing", "learning.course-version-not-found");
        var sessionId = Json.Required(start.Body, "id", "Starting a session");
        if (start.Status == System.Net.HttpStatusCode.Created)
        {
            count.Add();
        }
        else
        {
            // A resumed session: an examination that ended is never touched again; progress after exhaustion would open a new round.
            var examination = await edge.SendAsync(learner, HttpMethod.Get, $"/assessment/examinations/by-session/{sessionId}", null, cancellationToken);
            if (examination.Succeeded && Json.Text(examination.Body, "status") is "Exhausted" or "Passed")
            {
                await SettleAsync(learner, outcome, pharmacyId, version, course, cancellationToken);
                return;
            }
        }

        var chapters = Json.Items(start.Body, "chapters").OrderBy(chapter => Json.Int(chapter, "position")).ToList();
        foreach (var chapter in (outcome == DemoOutcome.Started ? chapters.Take(1) : chapters).Where(chapter => !Json.Bool(chapter, "completed")))
        {
            var progress = new JsonObject { ["chapterId"] = Json.Required(chapter, "chapterId", "The session"), ["percent"] = 100 };
            await edge.CallAsync(learner, HttpMethod.Post, $"/learning/sessions/{sessionId}/progress", progress, cancellationToken);
        }

        if (outcome != DemoOutcome.Started)
        {
            await ExamineAsync(learner, sessionId, outcome, course, cancellationToken);
        }

        await SettleAsync(learner, outcome, pharmacyId, version, course, cancellationToken);
    }

    /// <summary>Takes attempts until the outcome is reached: passed, one attempt failed, or every attempt failed.</summary>
    private async Task ExamineAsync(string learner, string sessionId, DemoOutcome outcome, DemoCourse course, CancellationToken cancellationToken)
    {
        const int MostRounds = 10;
        for (var round = 0; round < MostRounds; round++)
        {
            var examination = await poller.UntilAsync($"The examination of {learner}'s session", async token =>
            {
                var answer = await edge.SendAsync(learner, HttpMethod.Get, $"/assessment/examinations/by-session/{sessionId}", null, token);
                if (answer.Code == "assessment.examination-not-found")
                {
                    return Polled<JsonNode>.Wait("not found");
                }

                if (!answer.Succeeded || answer.Body is null)
                {
                    throw EdgeClient.Failure(learner, HttpMethod.Get, $"/assessment/examinations/by-session/{sessionId}", answer);
                }

                return Json.Text(answer.Body, "status") == "Locked" ? Polled<JsonNode>.Wait("Locked") : Polled<JsonNode>.Done(answer.Body);
            }, cancellationToken);

            var status = Json.Text(examination, "status");
            if (status is "Passed" or "Exhausted" || (outcome == DemoOutcome.Failed && status == "Unlocked" && Json.Int(examination, "attemptsUsed") >= 1))
            {
                return;
            }

            var examinationId = Json.Required(examination, "id", "The examination");
            var attempt = status == "InAttempt"
                ? examination["currentAttempt"]
                : (await edge.CallAsync(learner, HttpMethod.Post, $"/assessment/examinations/{examinationId}/attempts", null, cancellationToken)).Body;
            foreach (var question in Json.Items(attempt, "questions"))
            {
                var answer = new JsonObject { ["optionIds"] = new JsonArray(Choose(course, question, pass: outcome == DemoOutcome.Passed)) };
                await edge.CallAsync(learner, HttpMethod.Put, $"/assessment/examinations/{examinationId}/attempts/current/answers/{Json.Required(question, "id", "The attempt")}", answer, cancellationToken);
            }

            await edge.CallAsync(learner, HttpMethod.Post, $"/assessment/examinations/{examinationId}/attempts/current/submit", null, cancellationToken);
            count.Add();
        }

        throw new SeedingException($"{learner}'s examination of {course.Title} did not reach {outcome} within {MostRounds} rounds.");
    }

    /// <summary>The attempt's option the plan marks correct (to pass) or a wrong one (to fail), found by the question's and the option's text.</summary>
    private static string Choose(DemoCourse course, JsonNode question, bool pass)
    {
        var text = Json.Text(question, "text");
        var planned = course.Chapters.Concat(course.SecondVersionChapters).SelectMany(chapter => chapter.Questions).FirstOrDefault(each => each.Text == text)
            ?? throw new SeedingException($"The attempt asks '{text}', which {course.Title} does not plan.");
        var wanted = planned.Options.Where(option => option.Correct == pass).Select(option => option.Text).ToHashSet(StringComparer.Ordinal);
        var option = Json.Items(question, "options").FirstOrDefault(each => wanted.Contains(Json.Text(each, "text") ?? string.Empty))
            ?? throw new SeedingException($"The attempt shows no option of '{text}' the plan names.");
        return Json.Required(option, "id", "The attempt");
    }

    /// <summary>Waits until the outcome shows: the certificate with its document, and Compliance's assignment state.</summary>
    private async Task SettleAsync(string learner, DemoOutcome outcome, string pharmacyId, SeededVersion version, DemoCourse course, CancellationToken cancellationToken)
    {
        if (outcome == DemoOutcome.Passed)
        {
            await poller.UntilAsync($"{learner}'s certificate of {course.Title}", async token =>
                await HasCertificateAsync(learner, course.Title, requireDocument: true, token) ? Polled<string>.Done(course.Title) : Polled<string>.Wait("not listed with its document"), cancellationToken);
        }

        var expected = outcome switch
        {
            DemoOutcome.Exhausted => "Locked",
            DemoOutcome.Passed => null,
            _ => "InProgress",
        };
        await poller.UntilAsync($"{learner}'s assignment of {course.Title} in Compliance", async token =>
        {
            var state = Json.Text(await AssignmentAsync(learner, pharmacyId, version.CourseId, token), "state");
            // A passed assignment leaves the active list, or makes way for its follow-up, which is open (docs/adr/0026).
            var settled = expected is null ? state is null or "Open" or "Completed" or "CompletedLate" : state == expected;
            return settled ? Polled<string>.Done(expected ?? "Completed") : Polled<string>.Wait(state ?? "not listed");
        }, cancellationToken);
    }

    private async Task<bool> HasCertificateAsync(string learner, string title, bool requireDocument, CancellationToken cancellationToken) =>
        (await edge.AllAsync(learner, "/certification/me/certificates", cancellationToken))
            .Any(certificate => Json.Text(certificate, "courseTitle") == title && (!requireDocument || Json.Bool(certificate, "documentReady")));

    /// <summary>The learner's active assignment of the course in the pharmacy, or null.</summary>
    private async Task<JsonNode?> AssignmentAsync(string learner, string pharmacyId, string courseId, CancellationToken cancellationToken) =>
        (await edge.AllAsync(learner, $"/compliance/me/assignments?pharmacyId={pharmacyId}", cancellationToken))
            .FirstOrDefault(assignment => Json.Text(assignment, "courseId") == courseId);

    private string NameOf(string username) => users.TryGetValue(username, out var user) ? $"{user.FirstName} {user.LastName}" : username;
}
