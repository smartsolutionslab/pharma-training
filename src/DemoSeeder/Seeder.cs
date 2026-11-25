namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Walks the plan through the edge, Keycloak and Mailpit, each step looking up before it acts: the users, the pharmacies
/// and their members, every course's first version, the scenarios on first versions, the second versions, then the
/// scenarios on those, so a first version is passed before its successor supersedes it. Answers how much it created.
/// </summary>
internal static class Seeder
{
    public static async Task<int> SeedAsync(SeedPlan plan, SeederSettings settings, HttpClient http, TimeProvider time, TextWriter output, CancellationToken cancellationToken)
    {
        var configuration = await EdgeClient.ConfigurationAsync(http, settings.EdgeUrl, cancellationToken);
        var authority = new Uri(Json.Required(configuration, "oidcAuthority", "GET /api/config.json"));
        var clientId = Json.Required(configuration, "clientId", "GET /api/config.json");
        var count = new SeedCount();
        var poller = new Poller(time, settings.PollTimeout);

        var administration = new KeycloakUsers(http, settings, authority);
        foreach (var user in plan.Users)
        {
            if (await administration.EnsureAsync(user, cancellationToken))
            {
                count.Add();
            }
        }

        var signIn = new KeycloakSignIn(http, authority, clientId, settings.WebOrigin);
        var edge = new EdgeClient(http, settings.EdgeUrl, (user, token) => signIn.SignInAsync(user, settings.PasswordOf(user), token), poller);
        var users = plan.Users.ToDictionary(user => user.Username, StringComparer.Ordinal);

        var pharmacies = new PharmacySeeding(edge, new Mailbox(http, settings.MailpitUrl, poller), users, count, output);
        foreach (var pharmacy in plan.Pharmacies)
        {
            await pharmacies.EnsureAsync(pharmacy, cancellationToken);
        }

        var courses = new CourseSeeding(plan.Editors, edge, count, output);
        var scenarios = new ScenarioSeeding(plan, edge, poller, pharmacies, courses, time, count, output);
        foreach (var number in new[] { 1, 2 })
        {
            foreach (var course in plan.Courses.Where(course => number == 1 || course.SecondVersionChapters.Count > 0))
            {
                await courses.EnsureAsync(course, number, cancellationToken);
            }

            foreach (var scenario in plan.Scenarios.Where(scenario => scenario.CourseVersion == number))
            {
                await scenarios.DriveAsync(scenario, cancellationToken);
            }
        }

        return count.Created;
    }
}
